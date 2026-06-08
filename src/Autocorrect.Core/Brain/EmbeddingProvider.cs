namespace Autocorrect.Core.Brain;

public interface IEmbeddingProvider
{
    bool IsSemantic { get; }

    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken);
}

// Uses Ollama embeddings when reachable; returns null to let the store fall back to keyword scoring.
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly IOllamaClient _client;

    public OllamaEmbeddingProvider(IOllamaClient client)
    {
        _client = client;
    }

    public bool IsSemantic => true;

    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken) => _client.EmbedAsync(text, cancellationToken);
}

// No-op provider that signals keyword-only mode when embeddings are unavailable.
public sealed class KeywordEmbeddingProvider : IEmbeddingProvider
{
    public bool IsSemantic => false;

    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken) => Task.FromResult<float[]?>(null);
}
