using System.IO;
using System.Text.Json;

namespace Autocorrect.Core.Brain;

public sealed class VectorDocument
{
    public string Id { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public FileRole Role { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float[]? Vector { get; set; }
}

public sealed record VectorSearchResult(VectorDocument Document, double Score, string Reason);

public interface IVectorStore
{
    Task AddDocumentsAsync(IEnumerable<VectorDocument> docs, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string query, int topK, CancellationToken cancellationToken);

    Task ClearProjectAsync(string projectRoot, CancellationToken cancellationToken);
}

// File-backed store: cosine similarity when embeddings exist, keyword overlap otherwise. TODO: swap for SQLite/ANN at scale.
public sealed class FileVectorStore : IVectorStore
{
    private readonly string _directory;
    private readonly string _projectRoot;
    private readonly IEmbeddingProvider _embeddings;
    private readonly object _gate = new();
    private List<VectorDocument> _documents;

    public FileVectorStore(string baseDirectory, string projectRoot, IEmbeddingProvider embeddings)
    {
        _projectRoot = projectRoot;
        _embeddings = embeddings;
        _directory = Path.Combine(baseDirectory, "vectors");
        Directory.CreateDirectory(_directory);
        _documents = Load();
    }

    private string FilePath => Path.Combine(_directory, $"vectors-{BrainStorage.ProjectKey(_projectRoot)}.json");

    public async Task AddDocumentsAsync(IEnumerable<VectorDocument> docs, CancellationToken cancellationToken)
    {
        var prepared = new List<VectorDocument>();
        foreach (var doc in docs)
        {
            doc.Vector = _embeddings.IsSemantic ? await _embeddings.EmbedAsync(doc.Text, cancellationToken) : null;
            prepared.Add(doc);
        }

        lock (_gate)
        {
            _documents = prepared;
            Save();
        }
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(string query, int topK, CancellationToken cancellationToken)
    {
        List<VectorDocument> snapshot;
        lock (_gate)
        {
            snapshot = _documents.ToList();
        }

        if (snapshot.Count == 0)
        {
            return Array.Empty<VectorSearchResult>();
        }

        var queryVector = _embeddings.IsSemantic ? await _embeddings.EmbedAsync(query, cancellationToken) : null;
        var useSemantic = queryVector is not null && snapshot.Any(d => d.Vector is { Length: > 0 });

        var scored = snapshot.Select(doc =>
        {
            var (score, reason) = useSemantic && doc.Vector is { Length: > 0 }
                ? (Cosine(queryVector!, doc.Vector), "semantic match")
                : (KeywordScore(query, doc), "keyword match");
            return new VectorSearchResult(doc, score, reason);
        });

        return scored
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    public Task ClearProjectAsync(string projectRoot, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _documents = new List<VectorDocument>();
            var path = Path.Combine(_directory, $"vectors-{BrainStorage.ProjectKey(projectRoot)}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        return Task.CompletedTask;
    }

    private static double Cosine(float[] a, float[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA <= 0 || normB <= 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static double KeywordScore(string query, VectorDocument doc)
    {
        var terms = Tokenize(query);
        if (terms.Count == 0)
        {
            return 0;
        }

        var haystack = $"{doc.Path} {doc.Summary} {doc.Text}".ToLowerInvariant();
        var pathTokens = Tokenize(doc.Path);
        double score = 0;
        foreach (var term in terms)
        {
            if (pathTokens.Contains(term))
            {
                score += 2.5;
            }
            else if (haystack.Contains(term, StringComparison.Ordinal))
            {
                score += 1.0;
            }
        }

        return score / terms.Count;
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(new[] { ' ', '/', '\\', '.', '-', '_', ',', ':', ';', '(', ')', '\n', '\t', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);
    }

    private List<VectorDocument> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<VectorDocument>>(File.ReadAllText(FilePath), BrainJson.Options) ?? new List<VectorDocument>()
                : new List<VectorDocument>();
        }
        catch
        {
            return new List<VectorDocument>();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_documents, BrainJson.Options));
        }
        catch
        {
            // Vector persistence is best-effort and must never crash the app.
        }
    }
}
