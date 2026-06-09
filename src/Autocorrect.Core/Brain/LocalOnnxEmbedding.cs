using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Autocorrect.Core.Brain;

// Downloads and locates the bge-small ONNX model + vocab so the app embeds in-process (no Python/Docker).
public sealed class OnnxModelAssets
{
    private const string ModelUrl = "https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/main/onnx/model.onnx";
    private const string VocabUrl = "https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/main/vocab.txt";

    public OnnxModelAssets(string baseDirectory)
    {
        Directory = Path.Combine(baseDirectory, "models", "bge-small-en-v1.5");
        ModelPath = Path.Combine(Directory, "model.onnx");
        VocabPath = Path.Combine(Directory, "vocab.txt");
    }

    public string Directory { get; }
    public string ModelPath { get; }
    public string VocabPath { get; }

    public bool IsDownloaded => File.Exists(ModelPath) && File.Exists(VocabPath) &&
                                new FileInfo(ModelPath).Length > 0 && new FileInfo(VocabPath).Length > 0;

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        System.IO.Directory.CreateDirectory(Directory);
        await DownloadIfMissingAsync(VocabUrl, VocabPath, cancellationToken);
        await DownloadIfMissingAsync(ModelUrl, ModelPath, cancellationToken);
    }

    private static async Task DownloadIfMissingAsync(string url, string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var temp = path + ".download";
        await using (var file = File.Create(temp))
        {
            await response.Content.CopyToAsync(file, cancellationToken);
        }

        File.Move(temp, path, true);
    }
}

// Minimal BERT WordPiece tokenizer (uncased) so we need no external tokenizer dependency.
public sealed class WordPieceTokenizer
{
    private readonly Dictionary<string, long> _vocab = new(StringComparer.Ordinal);
    private readonly long _cls;
    private readonly long _sep;
    private readonly long _unk;
    private readonly int _maxTokens;

    public WordPieceTokenizer(string vocabPath, int maxTokens)
    {
        var index = 0L;
        foreach (var raw in File.ReadLines(vocabPath))
        {
            var token = raw.TrimEnd('\n', '\r').TrimStart('\uFEFF');
            _vocab[token] = index++;
        }

        _cls = Lookup("[CLS]");
        _sep = Lookup("[SEP]");
        _unk = Lookup("[UNK]");
        _maxTokens = Math.Clamp(maxTokens, 16, 512);
    }

    // Produces token ids and attention mask with [CLS]/[SEP] wrapping, truncated to max length.
    public (long[] Ids, long[] Mask) Encode(string text)
    {
        var ids = new List<long> { _cls };
        foreach (var word in BasicTokenize(text))
        {
            foreach (var piece in WordPieces(word))
            {
                ids.Add(piece);
            }
        }

        if (ids.Count > _maxTokens - 1)
        {
            ids = ids.GetRange(0, _maxTokens - 1);
        }

        ids.Add(_sep);
        var idArray = ids.ToArray();
        var mask = new long[idArray.Length];
        Array.Fill(mask, 1L);
        return (idArray, mask);
    }

    private long Lookup(string token) => _vocab.TryGetValue(token, out var id) ? id : 0L;

    private static IEnumerable<string> BasicTokenize(string text)
    {
        var cleaned = StripAccents(text.ToLowerInvariant());
        var current = new StringBuilder();
        foreach (var ch in cleaned)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
            }
            else if (char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                yield return ch.ToString();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private IEnumerable<long> WordPieces(string word)
    {
        if (word.Length == 0)
        {
            yield break;
        }

        if (word.Length > 100)
        {
            yield return _unk;
            yield break;
        }

        var pieces = new List<long>();
        var start = 0;
        while (start < word.Length)
        {
            var end = word.Length;
            long? matched = null;
            while (start < end)
            {
                var candidate = (start > 0 ? "##" : string.Empty) + word[start..end];
                if (_vocab.TryGetValue(candidate, out var id))
                {
                    matched = id;
                    break;
                }

                end--;
            }

            if (matched is null)
            {
                yield return _unk;
                yield break;
            }

            pieces.Add(matched.Value);
            start = end;
        }

        foreach (var piece in pieces)
        {
            yield return piece;
        }
    }

    private static string StripAccents(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

// In-process embedder: runs bge-small via ONNX Runtime with CLS pooling + L2 normalization. No Python, no server.
public sealed class LocalOnnxEmbeddingService : IEmbeddingService
{
    public const int DefaultDimension = 384;

    private readonly OnnxModelAssets _assets;
    private readonly int _maxTokens;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private InferenceSession? _session;
    private WordPieceTokenizer? _tokenizer;
    private int _dimension = DefaultDimension;
    private string _lastError = string.Empty;

    public LocalOnnxEmbeddingService(string baseDirectory, int maxTokens = 256)
    {
        _assets = new OnnxModelAssets(baseDirectory);
        _maxTokens = maxTokens;
    }

    public string GetModelName() => "BAAI/bge-small-en-v1.5 (onnx, in-process)";

    public int GetVectorDimension() => _dimension;

    public string LastError => _lastError;

    public string ModelPath => _assets.ModelPath;

    public bool IsDownloaded => _assets.IsDownloaded;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureInitializedAsync(cancellationToken);
            var probe = await EmbedTextAsync("local embedding health check", cancellationToken);
            return probe is { Length: > 0 };
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }
    }

    public async Task<float[]?> EmbedTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        await EnsureInitializedAsync(cancellationToken);
        return Embed(text);
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken)
    {
        var list = texts.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
        if (list.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        await EnsureInitializedAsync(cancellationToken);
        var output = new List<float[]>(list.Count);
        foreach (var text in list)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = Embed(text);
            if (vector is { Length: > 0 })
            {
                output.Add(vector);
            }
        }

        return output;
    }

    private float[] Embed(string text)
    {
        var (ids, mask) = _tokenizer!.Encode(text);
        var length = ids.Length;
        var idsTensor = new DenseTensor<long>(new[] { 1, length });
        var maskTensor = new DenseTensor<long>(new[] { 1, length });
        var typeTensor = new DenseTensor<long>(new[] { 1, length });
        for (var i = 0; i < length; i++)
        {
            idsTensor[0, i] = ids[i];
            maskTensor[0, i] = mask[i];
            typeTensor[0, i] = 0;
        }

        var inputs = new List<NamedOnnxValue>();
        foreach (var name in _session!.InputMetadata.Keys)
        {
            inputs.Add(name switch
            {
                "attention_mask" => NamedOnnxValue.CreateFromTensor(name, maskTensor),
                "token_type_ids" => NamedOnnxValue.CreateFromTensor(name, typeTensor),
                _ => NamedOnnxValue.CreateFromTensor(name, idsTensor)
            });
        }

        using var results = _session.Run(inputs);
        var tensor = results.First().AsTensor<float>();
        var dims = tensor.Dimensions;
        var dim = dims[^1];
        var vector = new float[dim];
        // CLS pooling: first token. Handle pooled [1,dim] and sequence [1,seq,dim] outputs.
        for (var k = 0; k < dim; k++)
        {
            vector[k] = dims.Length == 2 ? tensor[0, k] : tensor[0, 0, k];
        }

        _dimension = dim;
        return Normalize(vector);
    }

    private static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (var value in vector)
        {
            sum += value * (double)value;
        }

        var norm = Math.Sqrt(sum);
        if (norm <= 0)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }

        return vector;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_session is not null && _tokenizer is not null)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null && _tokenizer is not null)
            {
                return;
            }

            await _assets.EnsureAsync(cancellationToken);
            _tokenizer = new WordPieceTokenizer(_assets.VocabPath, _maxTokens);
            _session = new InferenceSession(_assets.ModelPath);
        }
        finally
        {
            _initGate.Release();
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _initGate.Dispose();
    }
}

// Wraps the local ONNX service for the keyword/file vector store so it can do real semantic search offline.
public sealed class LocalOnnxEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingService _service;

    public LocalOnnxEmbeddingProvider(IEmbeddingService service)
    {
        _service = service;
    }

    public bool IsSemantic => true;

    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken) =>
        _service.EmbedTextAsync(text, cancellationToken);
}
