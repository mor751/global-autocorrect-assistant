namespace Autocorrect.Core.Brain;

// Single source of truth for which embedding models are valid with FastEmbed vs Ollama.
public static class FastEmbedModelCatalog
{
    public const string DefaultModel = "BAAI/bge-small-en-v1.5";
    public const int DefaultDimension = 384;

    public static IReadOnlyDictionary<string, int> KnownModels { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["BAAI/bge-small-en-v1.5"] = 384,
        ["BAAI/bge-base-en-v1.5"] = 768,
        ["sentence-transformers/all-MiniLM-L6-v2"] = 384,
        ["intfloat/multilingual-e5-large"] = 1024,
        ["jinaai/jina-embeddings-v2-base-en"] = 768
    };

    // Embedding models that belong to Ollama and must never be sent to FastEmbed.
    public static IReadOnlySet<string> OllamaOnlyModels { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "nomic-embed-text",
        "mxbai-embed-large",
        "all-minilm",
        "snowflake-arctic-embed"
    };

    public static bool IsOllamaOnly(string? model) =>
        !string.IsNullOrWhiteSpace(model) && OllamaOnlyModels.Contains(model.Trim());

    // Returns a FastEmbed-safe model: keeps valid names, replaces blank or Ollama-only names with the default.
    public static string Coerce(string? model)
    {
        if (string.IsNullOrWhiteSpace(model) || IsOllamaOnly(model))
        {
            return DefaultModel;
        }

        return model.Trim();
    }
}
