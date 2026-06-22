namespace Autocorrect.Core.Brain;

public static class BrainOptionsFactory
{
    public static ProjectBrainOptions FromSettings(CorrectionSettings settings) => new()
    {
        Ollama = new OllamaSettings(settings.AiEndpoint, settings.WriterModel, settings.OllamaEmbeddingModel),
        RetrievalTopK = settings.RetrievalTopK,
        Index = new IndexOptions
        {
            MaxFileSizeBytes = settings.MaxIndexedFileSizeKb * 1024,
            MaxFiles = settings.MaxIndexedFiles,
            MaxInitialChunks = settings.MaxInitialChunks,
            IgnoredFolders = settings.IgnoredProjectFolders.ToHashSet(StringComparer.OrdinalIgnoreCase)
        }
    };
}
