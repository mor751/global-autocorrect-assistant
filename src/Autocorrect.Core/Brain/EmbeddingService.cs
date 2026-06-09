namespace Autocorrect.Core.Brain;

public interface IEmbeddingService : IDisposable
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<float[]?> EmbedTextAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<float[]>> EmbedBatchAsync(IEnumerable<string> texts, CancellationToken cancellationToken);

    string GetModelName();

    int GetVectorDimension();
}

// Health snapshot for the in-process ONNX embedder (no sidecar, no Python).
public sealed class EmbeddingDiagnostics
{
    public bool Available { get; set; }
    public bool ModelDownloaded { get; set; }
    public string ModelName { get; set; } = "BAAI/bge-small-en-v1.5";
    public int VectorDimension { get; set; }
    public string ModelPath { get; set; } = string.Empty;
    public DateTimeOffset LastHealthCheck { get; set; }
    public string LastError { get; set; } = string.Empty;

    public string Summary()
    {
        if (Available)
        {
            return $"Local embedder ready: {ModelName}, dim {VectorDimension} (in-process ONNX).";
        }

        return string.IsNullOrWhiteSpace(LastError)
            ? "Local embedder unavailable."
            : $"Local embedder unavailable: {LastError}";
    }
}
