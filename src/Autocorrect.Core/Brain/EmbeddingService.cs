using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Autocorrect.Core.Brain;

public interface IEmbeddingService : IDisposable
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<float[]?> EmbedTextAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken);

    string GetModelName();

    int GetVectorDimension();
}

public sealed class FastEmbedDiagnostics
{
    public string SidecarUrl { get; set; } = "http://127.0.0.1:8765";
    public bool SidecarRunning { get; set; }
    public string PythonExecutable { get; set; } = string.Empty;
    public string PythonPath { get; set; } = string.Empty;
    public bool FastEmbedImportOk { get; set; }
    public bool ModelLoaded { get; set; }
    public string ModelName { get; set; } = "BAAI/bge-small-en-v1.5";
    public int VectorDimension { get; set; }
    public DateTimeOffset LastHealthCheck { get; set; }
    public string LastError { get; set; } = string.Empty;
    public bool TestEmbeddingOk { get; set; }

    public string Summary()
    {
        if (SidecarRunning && FastEmbedImportOk && ModelLoaded && VectorDimension == 384 && TestEmbeddingOk)
        {
            return $"FastEmbed ready: {ModelName}, dim {VectorDimension}, Python {PythonPath}";
        }

        var error = string.IsNullOrWhiteSpace(LastError) ? "unknown reason" : LastError;
        return $"FastEmbed unavailable: {error}";
    }
}

public sealed class FastEmbedEmbeddingService : IEmbeddingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _modelName;
    private readonly int _batchSize;
    private readonly string _sidecarUrl;
    private readonly string _pythonExecutable;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private Process? _process;
    private int _dimension;
    private string _lastError = string.Empty;

    public FastEmbedEmbeddingService(string modelName, int batchSize)
        : this(modelName, batchSize, "http://127.0.0.1:8765", "python")
    {
    }

    public FastEmbedEmbeddingService(string modelName, int batchSize, string sidecarUrl, string pythonExecutable)
    {
        _modelName = string.IsNullOrWhiteSpace(modelName) ? "BAAI/bge-small-en-v1.5" : modelName;
        _batchSize = Math.Clamp(batchSize, 1, 128);
        _sidecarUrl = string.IsNullOrWhiteSpace(sidecarUrl) ? "http://127.0.0.1:8765" : sidecarUrl.TrimEnd('/');
        _pythonExecutable = string.IsNullOrWhiteSpace(pythonExecutable) ? "python" : pythonExecutable;
        _httpClient = new HttpClient { BaseAddress = new Uri(_sidecarUrl), Timeout = TimeSpan.FromSeconds(45) };
    }

    public string GetModelName() => _modelName;

    public int GetVectorDimension() => _dimension;

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var diagnostics = await RunDiagnosticsAsync(loadModel: true, testEmbedding: false, cancellationToken);
        _dimension = diagnostics.VectorDimension;
        _lastError = diagnostics.LastError;
        return diagnostics.SidecarRunning &&
               diagnostics.FastEmbedImportOk &&
               diagnostics.ModelLoaded &&
               diagnostics.VectorDimension > 0;
    }

    public async Task<FastEmbedDiagnostics> RunDiagnosticsAsync(
        bool loadModel,
        bool testEmbedding,
        CancellationToken cancellationToken)
    {
        var diagnostics = new FastEmbedDiagnostics
        {
            SidecarUrl = _sidecarUrl,
            PythonExecutable = _pythonExecutable,
            ModelName = _modelName,
            LastHealthCheck = DateTimeOffset.UtcNow
        };

        try
        {
            await EnsureSidecarAsync(cancellationToken);

            var health = await GetJsonAsync("/health", cancellationToken);
            ApplyHealth(diagnostics, health);

            if (loadModel)
            {
                var info = await GetJsonAsync($"/model-info?model={Uri.EscapeDataString(_modelName)}", cancellationToken);
                ApplyHealth(diagnostics, info);
            }

            if (testEmbedding)
            {
                var vector = await EmbedTextAsync("test fastembed health", cancellationToken);
                diagnostics.TestEmbeddingOk = vector is { Length: 384 };
                if (vector is { Length: > 0 })
                {
                    diagnostics.VectorDimension = vector.Length;
                }
            }
        }
        catch (Exception ex)
        {
            diagnostics.LastError = ex.Message;
            _lastError = ex.Message;
        }

        return diagnostics;
    }

    public async Task<float[]?> EmbedTextAsync(string text, CancellationToken cancellationToken)
    {
        var batch = await EmbedBatchAsync(new[] { text }, cancellationToken);
        return batch.FirstOrDefault();
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken)
    {
        var list = texts.Where(text => !string.IsNullOrWhiteSpace(text)).ToList();
        if (list.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        await EnsureSidecarAsync(cancellationToken);
        var payload = new
        {
            model = _modelName,
            texts = list,
            batchSize = _batchSize
        };

        using var response = await _httpClient.PostAsJsonAsync("/embed-batch", payload, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _lastError = $"FastEmbed /embed-batch HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}";
            return Array.Empty<float[]>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            _lastError = ReadString(document.RootElement, "error");
            return Array.Empty<float[]>();
        }

        if (!document.RootElement.TryGetProperty("vectors", out var vectors) || vectors.ValueKind != JsonValueKind.Array)
        {
            _lastError = "FastEmbed response did not include vectors.";
            return Array.Empty<float[]>();
        }

        var output = new List<float[]>();
        foreach (var vector in vectors.EnumerateArray())
        {
            var values = vector.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            if (values.Length > 0)
            {
                _dimension = values.Length;
                output.Add(values);
            }
        }

        return output;
    }

    private async Task EnsureSidecarAsync(CancellationToken cancellationToken)
    {
        if (await IsSidecarReachableAsync(cancellationToken))
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (await IsSidecarReachableAsync(cancellationToken))
            {
                return;
            }

            var script = FindSidecarScript();
            if (script is null)
            {
                throw new FileNotFoundException("FastEmbed sidecar script was not found.");
            }

            var uri = new Uri(_sidecarUrl);
            var args =
                $"-u \"{script}\" --host {uri.Host} --port {uri.Port} --model \"{_modelName}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = _pythonExecutable,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _process = Process.Start(startInfo);
            if (_process is null)
            {
                throw new InvalidOperationException($"Could not start Python executable '{_pythonExecutable}'.");
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    var error = await _process.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        _lastError = error.Trim();
                    }
                }
                catch
                {
                    // Diagnostics only.
                }
            });

            var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await IsSidecarReachableAsync(cancellationToken))
                {
                    return;
                }

                if (_process.HasExited)
                {
                    var stderr = await _process.StandardError.ReadToEndAsync(cancellationToken);
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)
                        ? "FastEmbed sidecar process exited during startup."
                        : stderr.Trim());
                }

                await Task.Delay(450, cancellationToken);
            }

            throw new TimeoutException($"FastEmbed sidecar did not respond at {_sidecarUrl} within 25 seconds.");
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<bool> IsSidecarReachableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            using var response = await _httpClient.GetAsync("/health", linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonElement> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"FastEmbed {path} HTTP {(int)response.StatusCode}: {text}");
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void ApplyHealth(FastEmbedDiagnostics diagnostics, JsonElement element)
    {
        diagnostics.SidecarRunning = ReadBool(element, "ok") || ReadBool(element, "sidecarRunning");
        diagnostics.PythonPath = ReadString(element, "pythonExecutable");
        diagnostics.FastEmbedImportOk = ReadBool(element, "fastembedImportOk");
        diagnostics.ModelLoaded = ReadBool(element, "modelLoaded");
        diagnostics.ModelName = ReadString(element, "modelName", diagnostics.ModelName);
        diagnostics.VectorDimension = ReadInt(element, "dimension");
        diagnostics.LastError = ReadString(element, "lastError", diagnostics.LastError);
    }

    private static string? FindSidecarScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "fastembed_sidecar.py"),
            Path.Combine(AppContext.BaseDirectory, "Brain", "fastembed_sidecar.py"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Autocorrect.Core", "Brain", "fastembed_sidecar.py")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Autocorrect.Core", "Brain", "fastembed_sidecar.py"))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ReadString(JsonElement element, string name, string fallback = "")
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;
    }

    private static bool ReadBool(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static int ReadInt(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _startGate.Dispose();
    }
}
