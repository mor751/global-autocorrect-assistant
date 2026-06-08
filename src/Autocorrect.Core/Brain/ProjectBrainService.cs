using System.IO;
using System.Text.Json;

namespace Autocorrect.Core.Brain;

public enum EnhancementStatus
{
    ImprovedReady,
    MissingContext,
    NotIndexed,
    OllamaFallback
}

public sealed class EnhancementOutcome
{
    public string OriginalPrompt { get; set; } = string.Empty;
    public EnhancementStatus Status { get; set; }
    public PromptAnalysis Analysis { get; set; } = new();
    public EnhancedPromptResult Result { get; set; } = new();
    public ProjectBrainData? Brain { get; set; }
    public bool ProjectIndexed { get; set; }
    public bool OllamaAvailable { get; set; }
    public List<string> UsedFiles { get; set; } = new();
}

public sealed class ProjectBrainOptions
{
    public OllamaSettings Ollama { get; set; } = OllamaSettings.Default;
    public IndexOptions Index { get; set; } = new();
    public int RetrievalTopK { get; set; } = 6;
}

// Orchestrates indexing, retrieval, analysis, rewriting, and history into one project-aware enhancement flow.
public sealed class ProjectBrainService : IDisposable
{
    private readonly string _baseDirectory;
    private readonly ProjectBrainOptions _options;
    private readonly IProjectIndexer _indexer;
    private readonly OllamaClient _ollama;
    private readonly PromptHistoryStore _history;
    private readonly PromptAnalyzer _analyzer = new();

    public ProjectBrainService(string baseDirectory, ProjectBrainOptions options)
    {
        _baseDirectory = baseDirectory;
        _options = options;
        _indexer = new ProjectIndexer();
        _ollama = new OllamaClient(options.Ollama);
        _history = new PromptHistoryStore(BrainStorage.BrainDirectory(baseDirectory));
    }

    public bool IsIndexed(string? projectRoot) =>
        !string.IsNullOrWhiteSpace(projectRoot) && File.Exists(BrainPath(projectRoot!));

    public async Task<ProjectBrainData> IndexAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var brain = await Task.Run(() => _indexer.Index(projectRoot, _options.Index), cancellationToken);
        SaveBrain(brain);

        var embeddings = await CreateEmbeddingProviderAsync(cancellationToken);
        var store = new FileVectorStore(BrainStorage.BrainDirectory(_baseDirectory), projectRoot, embeddings);
        await store.ClearProjectAsync(projectRoot, cancellationToken);
        await store.AddDocumentsAsync(brain.Files.Select(ToDocument), cancellationToken);
        return brain;
    }

    public ProjectBrainData? LoadBrain(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !File.Exists(BrainPath(projectRoot)))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProjectBrainData>(File.ReadAllText(BrainPath(projectRoot)), BrainJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public async Task<EnhancementOutcome> EnhanceAsync(string originalPrompt, string? projectRoot, CancellationToken cancellationToken)
    {
        var brain = LoadBrain(projectRoot);
        var ollamaAvailable = await _ollama.IsAvailableAsync(cancellationToken);
        var preferences = _history.LearnPreferences();

        var retrieved = brain is null
            ? new List<RetrievedFile>()
            : await RetrieveAsync(originalPrompt, projectRoot!, brain, ollamaAvailable, cancellationToken);

        var analysis = _analyzer.Analyze(originalPrompt, brain, retrieved, preferences);
        var rewriter = new SmartPromptRewriter(_ollama);
        var result = await rewriter.BuildAsync(originalPrompt, analysis, brain, retrieved, preferences, ollamaAvailable, cancellationToken);

        var outcome = new EnhancementOutcome
        {
            OriginalPrompt = originalPrompt,
            Analysis = analysis,
            Result = result,
            Brain = brain,
            ProjectIndexed = brain is not null,
            OllamaAvailable = ollamaAvailable,
            UsedFiles = retrieved.Select(r => r.File.Path).ToList(),
            Status = DetermineStatus(brain is not null, ollamaAvailable, result.Kind)
        };

        _history.Record(new PromptHistoryEntry
        {
            ProjectRoot = projectRoot ?? string.Empty,
            OriginalPrompt = originalPrompt,
            EnhancedPrompt = result.ImprovedPrompt,
            RelevantFiles = outcome.UsedFiles,
            TaskType = analysis.TaskType
        });

        return outcome;
    }

    private async Task<List<RetrievedFile>> RetrieveAsync(
        string prompt,
        string projectRoot,
        ProjectBrainData brain,
        bool ollamaAvailable,
        CancellationToken cancellationToken)
    {
        var embeddings = ollamaAvailable ? await CreateEmbeddingProviderAsync(cancellationToken) : new KeywordEmbeddingProvider();
        var store = new FileVectorStore(BrainStorage.BrainDirectory(_baseDirectory), projectRoot, embeddings);
        var hits = await store.SearchAsync(prompt, _options.RetrievalTopK, cancellationToken);

        var byPath = brain.Files.ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        return hits
            .Where(h => byPath.ContainsKey(h.Document.Path))
            .Select(h => new RetrievedFile { File = byPath[h.Document.Path], Reason = h.Reason, Score = h.Score })
            .ToList();
    }

    private async Task<IEmbeddingProvider> CreateEmbeddingProviderAsync(CancellationToken cancellationToken)
    {
        if (!await _ollama.IsAvailableAsync(cancellationToken))
        {
            return new KeywordEmbeddingProvider();
        }

        var models = await _ollama.ListModelsAsync(cancellationToken);
        var hasEmbeddingModel = models.Any(m => m.StartsWith(_options.Ollama.EmbeddingModel, StringComparison.OrdinalIgnoreCase));
        return hasEmbeddingModel ? new OllamaEmbeddingProvider(_ollama) : new KeywordEmbeddingProvider();
    }

    private static EnhancementStatus DetermineStatus(bool indexed, bool ollamaAvailable, EnhancementKind kind)
    {
        if (!indexed)
        {
            return EnhancementStatus.NotIndexed;
        }

        if (kind == EnhancementKind.MissingContextWarning)
        {
            return EnhancementStatus.MissingContext;
        }

        return ollamaAvailable ? EnhancementStatus.ImprovedReady : EnhancementStatus.OllamaFallback;
    }

    private static VectorDocument ToDocument(ProjectFileSummary file) => new()
    {
        Id = file.Path,
        Path = file.Path,
        Role = file.Role,
        Summary = file.Summary,
        Text = $"{file.Path}\n{file.Summary}\n{string.Join(' ', file.Symbols)}\n{file.PreviewChunks.FirstOrDefault()}"
    };

    private string BrainPath(string projectRoot) =>
        Path.Combine(BrainStorage.BrainDirectory(_baseDirectory), $"brain-{BrainStorage.ProjectKey(projectRoot)}.json");

    private void SaveBrain(ProjectBrainData brain)
    {
        try
        {
            File.WriteAllText(BrainPath(brain.ProjectRoot), JsonSerializer.Serialize(brain, BrainJson.Options));
        }
        catch
        {
            // Brain persistence failure should not crash indexing.
        }
    }

    public PromptHistoryStore History => _history;

    public void Dispose() => _ollama.Dispose();
}
