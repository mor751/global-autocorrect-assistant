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
    public string ProjectRoot { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public PromptTargetAgent TargetAgent { get; set; } = PromptTargetAgent.Codex;
    public string ResolutionSource { get; set; } = string.Empty;
    public RetrievalMode RetrievalMode { get; set; } = RetrievalMode.NoBrain;
    public int OriginalTokenEstimate { get; set; }
    public int ImprovedTokenEstimate { get; set; }
    public int DownstreamTokenSavingsEstimate { get; set; }
    public string RecommendedModels { get; set; } = string.Empty;
    public long VectorCount { get; set; }
}

public sealed class ProjectBrainOptions
{
    public OllamaSettings Ollama { get; set; } = OllamaSettings.Default;
    public IndexOptions Index { get; set; } = new();
    public int RetrievalTopK { get; set; } = 12;
}

// Orchestrates indexing, retrieval, analysis, rewriting, and history into one project-aware enhancement flow.
public sealed class ProjectBrainService : IDisposable
{
    private const int EmbeddingBatchSize = 16;

    private readonly string _baseDirectory;
    private ProjectBrainOptions _options;
    private readonly IProjectIndexer _indexer;
    private OllamaClient _ollama;
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

    // Re-applies current settings (Ollama endpoint, retrieval depth, index limits) so a GUI refresh truly reruns everything.
    public void Reconfigure(ProjectBrainOptions options)
    {
        var ollamaChanged = !_options.Ollama.Equals(options.Ollama);
        _options = options;
        if (!ollamaChanged)
        {
            return;
        }

        var old = _ollama;
        _ollama = new OllamaClient(options.Ollama);
        old.Dispose();
    }

    public bool IsIndexed(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return false;
        }

        var root = NormalizeProjectRoot(projectRoot);
        if (File.Exists(BrainPath(root)))
        {
            return true;
        }

        var metadata = LoadIndexMetadata(root);
        return metadata is not null && metadata.IndexedFiles > 0;
    }

    public static string NormalizeProjectRoot(string projectRoot) =>
        Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public IReadOnlyList<string> ListIndexedProjects()
    {
        var directory = BrainStorage.BrainDirectory(_baseDirectory);
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var roots = new List<string>();
        foreach (var file in Directory.EnumerateFiles(directory, "brain-*.json"))
        {
            try
            {
                var brain = JsonSerializer.Deserialize<ProjectBrainData>(File.ReadAllText(file), BrainJson.Options);
                if (!string.IsNullOrWhiteSpace(brain?.ProjectRoot))
                {
                    roots.Add(brain.ProjectRoot);
                }
            }
            catch
            {
                // Ignore corrupt brain files when listing projects.
            }
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ProjectBrainData> IndexAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var snapshot = await Task.Run(() =>
        {
            return _indexer is ProjectIndexer concrete
                ? concrete.IndexDetailed(projectRoot, _options.Index)
                : new ProjectIndexSnapshot { Brain = _indexer.Index(projectRoot, _options.Index) };
        }, cancellationToken);
        var brain = snapshot.Brain;
        SaveBrain(brain);
        SaveSkippedReport(projectRoot, snapshot.SkippedDetails);

        var fallbackStore = CreateFallbackStore(projectRoot);
        await fallbackStore.ClearProjectAsync(projectRoot, cancellationToken);
        await fallbackStore.AddDocumentsAsync(brain.Files.Select(ToDocument), cancellationToken);

        var previous = LoadIndexMetadata(projectRoot);
        var metadata = CreateMetadata(projectRoot, brain, snapshot, previous);
        metadata.Status = ProjectBrainStatus.SemanticIndexing;
        metadata.QuickBrainStatus = ProjectBrainStatus.Ready;
        SaveIndexMetadata(metadata);

        using var embeddings = CreateEmbeddingService();
        using var store = CreateVectorStore();

        if (!await embeddings.IsAvailableAsync(cancellationToken))
        {
            metadata.Status = ProjectBrainStatus.EmbeddingUnavailable;
            metadata.SemanticBrainStatus = ProjectBrainStatus.EmbeddingUnavailable;
            metadata.LastError = "Local embedding model could not be loaded or downloaded. Check internet access for the first-time model download.";
            SaveIndexMetadata(metadata);
            return brain;
        }

        var dimension = embeddings.GetVectorDimension();
        metadata.VectorDimension = dimension;
        if (!await store.EnsureCollectionAsync(metadata.Collection, dimension, cancellationToken))
        {
            metadata.Status = ProjectBrainStatus.VectorStoreUnavailable;
            metadata.SemanticBrainStatus = ProjectBrainStatus.VectorStoreUnavailable;
            metadata.LastError = "Local SQLite vector store could not be initialized.";
            SaveIndexMetadata(metadata);
            return brain;
        }

        await store.UpsertSymbolGraphAsync(metadata.Collection, brain.Graph, cancellationToken);

        var previousFiles = previous?.FileHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var previousChunks = previous?.ChunkHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentFiles = snapshot.Chunks.Select(chunk => chunk.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var deleted in previousFiles.Keys.Where(path => !currentFiles.Contains(path)).ToList())
        {
            await store.DeleteFileAsync(metadata.Collection, deleted, cancellationToken);
        }

        var chunksToEmbed = snapshot.Chunks
            .Where(chunk =>
                !previousFiles.TryGetValue(chunk.FilePath, out var oldFileHash) ||
                !oldFileHash.Equals(chunk.FileHash, StringComparison.OrdinalIgnoreCase) ||
                !previousChunks.TryGetValue(chunk.Id, out var oldChunkHash) ||
                !oldChunkHash.Equals(chunk.ChunkHash, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var batch in chunksToEmbed.Chunk(EmbeddingBatchSize))
        {
            var vectors = await embeddings.EmbedBatchAsync(batch.Select(chunk => $"passage: {chunk.EmbeddedText()}"), cancellationToken);
            if (vectors.Count != batch.Length)
            {
                metadata.FailedChunks += batch.Length;
                continue;
            }

            await store.UpsertAsync(metadata.Collection, batch, vectors, cancellationToken);
            metadata.EmbeddedChunks += batch.Length;
            SaveIndexMetadata(metadata);
        }

        metadata.Status = metadata.FailedChunks > 0 ? ProjectBrainStatus.PartialReady : ProjectBrainStatus.Ready;
        metadata.SemanticBrainStatus = metadata.Status;
        metadata.LastError = metadata.FailedChunks > 0 ? $"{metadata.FailedChunks} chunks failed to embed." : null;
        metadata.FileHashes = snapshot.Chunks
            .GroupBy(chunk => chunk.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().FileHash, StringComparer.OrdinalIgnoreCase);
        metadata.ChunkHashes = snapshot.Chunks.ToDictionary(chunk => chunk.Id, chunk => chunk.ChunkHash, StringComparer.OrdinalIgnoreCase);
        metadata.LastIndexedAt = DateTimeOffset.UtcNow;
        SaveIndexMetadata(metadata);
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

    public async Task<EnhancementOutcome> EnhanceAsync(
        string originalPrompt,
        string? projectRoot,
        CancellationToken cancellationToken,
        PromptTargetAgent targetAgent = PromptTargetAgent.Codex)
    {
        var brain = LoadBrain(projectRoot);
        var ollamaAvailable = await _ollama.IsAvailableAsync(cancellationToken);
        var preferences = _history.LearnPreferences();

        var retrieval = brain is null
            ? new RetrievalResponse { Query = originalPrompt, RetrievalMode = RetrievalMode.NoBrain }
            : await RetrieveDetailedAsync(originalPrompt, projectRoot!, brain, cancellationToken);
        var retrieved = ToRetrievedFiles(retrieval, brain);

        var analysis = _analyzer.Analyze(originalPrompt, brain, retrieved, preferences);
        var compiler = new PromptCompilerService(_ollama);
        var compiled = await compiler.CompileAsync(new PromptCompilerRequest
        {
            OriginalPrompt = originalPrompt,
            TargetAgent = targetAgent,
            Brain = brain,
            Retrieval = retrieval,
            MissingContext = analysis.MissingContext
        }, ollamaAvailable, cancellationToken);
        var result = compiler.ToEnhancedPromptResult(compiled, originalPrompt);

        var outcome = new EnhancementOutcome
        {
            OriginalPrompt = originalPrompt,
            Analysis = analysis,
            Result = result,
            Brain = brain,
            ProjectIndexed = brain is not null,
            OllamaAvailable = ollamaAvailable,
            UsedFiles = compiled.RelevantFiles.Count > 0 ? compiled.RelevantFiles : retrieved.Select(r => r.File.Path).ToList(),
            Status = DetermineStatus(brain is not null, ollamaAvailable, result.Kind),
            ProjectRoot = projectRoot ?? string.Empty,
            ProjectName = string.IsNullOrWhiteSpace(projectRoot) ? string.Empty : Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            TargetAgent = targetAgent,
            RetrievalMode = retrieval.RetrievalMode,
            OriginalTokenEstimate = EstimateTokens(originalPrompt),
            ImprovedTokenEstimate = EstimateTokens(result.ImprovedPrompt),
            RecommendedModels = AgentModelAdvisor.Recommend(targetAgent)
        };
        outcome.DownstreamTokenSavingsEstimate = AgentModelAdvisor.EstimateDownstreamTokenSavings(
            originalPrompt,
            result.ImprovedPrompt,
            outcome.UsedFiles.Count);

        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var stats = await GetVectorStatsAsync(projectRoot, cancellationToken);
            outcome.VectorCount = stats.VectorCount;
        }

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

    public async Task<IReadOnlyList<RetrievedFile>> SearchAsync(
        string query,
        string? projectRoot,
        int topK,
        CancellationToken cancellationToken)
    {
        var brain = LoadBrain(projectRoot);
        if (brain is null || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RetrievedFile>();
        }

        var ollamaAvailable = await _ollama.IsAvailableAsync(cancellationToken);
        var retrieval = await RetrieveDetailedAsync(query, projectRoot!, brain, cancellationToken);
        return ToRetrievedFiles(retrieval, brain);
    }

    public async Task<RetrievalResponse> SearchDetailedAsync(
        string query,
        string? projectRoot,
        int topK,
        CancellationToken cancellationToken)
    {
        var brain = LoadBrain(projectRoot);
        if (brain is null || string.IsNullOrWhiteSpace(projectRoot))
        {
            return new RetrievalResponse { Query = query, RetrievalMode = RetrievalMode.NoBrain };
        }

        return await RetrieveDetailedAsync(query, projectRoot, brain, cancellationToken, topK);
    }

    private async Task<RetrievalResponse> RetrieveDetailedAsync(
        string prompt,
        string projectRoot,
        ProjectBrainData brain,
        CancellationToken cancellationToken,
        int? topK = null)
    {
        var metadata = LoadIndexMetadata(projectRoot);
        using var embeddings = CreateEmbeddingService();
        using var store = CreateVectorStore();
        var fallback = CreateFallbackStore(projectRoot);
        var retrieval = new RagRetrievalService(embeddings, store, fallback, metadata);
        return await retrieval.RetrieveAsync(prompt, brain, projectRoot, topK ?? _options.RetrievalTopK, cancellationToken);
    }

    private FileVectorStore CreateFallbackStore(string projectRoot) =>
        new(BrainStorage.BrainDirectory(_baseDirectory), projectRoot, new KeywordEmbeddingProvider());

    private static List<RetrievedFile> ToRetrievedFiles(RetrievalResponse retrieval, ProjectBrainData? brain)
    {
        if (brain is null)
        {
            return new List<RetrievedFile>();
        }

        var byPath = brain.Files.ToDictionary(f => f.Path, f => f, StringComparer.OrdinalIgnoreCase);
        return retrieval.Results
            .Where(result => byPath.ContainsKey(result.FilePath))
            .Select(result => new RetrievedFile
            {
                File = byPath[result.FilePath],
                Reason = result.Reason,
                Score = result.Score
            })
            .ToList();
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

    public ProjectIndexMetadata? LoadIndexMetadata(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        var path = BrainStorage.ProjectIndexPath(_baseDirectory, projectRoot);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ProjectIndexMetadata>(File.ReadAllText(path), BrainJson.Options)
                : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<VectorStoreStats> GetVectorStatsAsync(string? projectRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return new VectorStoreStats { IsAvailable = false, Error = "No project folder selected." };
        }

        var metadata = LoadIndexMetadata(projectRoot);
        using var store = CreateVectorStore();
        return await store.GetStatsAsync(ResolveCollection(projectRoot, metadata), cancellationToken);
    }

    // Loads every stored vector + metadata for a project so the UI can visualize the embedding space.
    public async Task<IReadOnlyList<VectorPoint>> ExportVectorsAsync(string? projectRoot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return Array.Empty<VectorPoint>();
        }

        var metadata = LoadIndexMetadata(projectRoot);
        using var store = CreateVectorStore();
        return await store.ExportAsync(ResolveCollection(projectRoot, metadata), cancellationToken);
    }

    private static string ResolveCollection(string projectRoot, ProjectIndexMetadata? metadata) =>
        string.IsNullOrWhiteSpace(metadata?.Collection)
            ? BrainStorage.CollectionName(projectRoot)
            : metadata!.Collection;

    public async Task<BrainDoctorReport> DoctorAsync(string? projectRoot, CancellationToken cancellationToken)
    {
        var root = string.IsNullOrWhiteSpace(projectRoot) ? string.Empty : Path.GetFullPath(projectRoot);
        var metadata = LoadIndexMetadata(root);
        var embedding = await TestEmbeddingAsync(cancellationToken);
        var ollamaAvailable = await _ollama.IsAvailableAsync(cancellationToken);
        var stats = await GetVectorStatsAsync(string.IsNullOrWhiteSpace(root) ? null : root, cancellationToken);
        var graphStats = (0, 0);
        if (!string.IsNullOrWhiteSpace(root) && stats.IsAvailable)
        {
            using var store = CreateVectorStore();
            graphStats = await store.GetSymbolGraphStatsAsync(ResolveCollection(root, metadata), cancellationToken);
        }

        return new BrainDoctorReport
        {
            ProjectRoot = root,
            ProjectIndexed = !string.IsNullOrWhiteSpace(root) && IsIndexed(root),
            Status = metadata?.Status ?? ProjectBrainStatus.NoFolder,
            RagMode = metadata?.CurrentRagMode() ?? "No brain",
            OllamaAvailable = ollamaAvailable,
            OllamaModel = _options.Ollama.ChatModel,
            EmbedderReady = embedding.Available,
            EmbedderDimension = embedding.VectorDimension,
            EmbedderDownloaded = embedding.ModelDownloaded,
            EmbedderError = string.IsNullOrWhiteSpace(embedding.LastError) ? null : embedding.LastError,
            VectorStoreReady = stats.IsAvailable,
            VectorCount = stats.VectorCount,
            Collection = stats.CollectionName,
            SymbolNodes = graphStats.Item1,
            SymbolEdges = graphStats.Item2,
            IndexedFiles = metadata?.IndexedFiles ?? 0,
            TotalChunks = metadata?.TotalChunks ?? 0,
            EmbeddedChunks = metadata?.EmbeddedChunks ?? 0,
            SkippedFiles = metadata?.SkippedFiles ?? 0,
            LastError = metadata?.LastError ?? stats.Error
        };
    }

    public async Task<EmbeddingDiagnostics> TestEmbeddingAsync(CancellationToken cancellationToken)
    {
        using var local = new LocalOnnxEmbeddingService(_baseDirectory);
        var available = await local.IsAvailableAsync(cancellationToken);
        return new EmbeddingDiagnostics
        {
            Available = available,
            ModelDownloaded = local.IsDownloaded,
            ModelName = local.GetModelName(),
            VectorDimension = local.GetVectorDimension(),
            ModelPath = local.ModelPath,
            LastHealthCheck = DateTimeOffset.UtcNow,
            LastError = available ? string.Empty : local.LastError
        };
    }

    private IEmbeddingService CreateEmbeddingService() => new LocalOnnxEmbeddingService(_baseDirectory);

    private IProjectVectorStore CreateVectorStore() => new SqliteVectorStore(_baseDirectory);

    private ProjectIndexMetadata CreateMetadata(
        string projectRoot,
        ProjectBrainData brain,
        ProjectIndexSnapshot snapshot,
        ProjectIndexMetadata? previous)
    {
        var projectId = BrainStorage.ProjectKey(projectRoot);
        return new ProjectIndexMetadata
        {
            ProjectId = projectId,
            ProjectRoot = Path.GetFullPath(projectRoot),
            ProjectName = brain.ProjectName,
            Framework = brain.Stack.Framework ?? "unknown",
            EmbeddingProvider = "LocalOnnx",
            EmbeddingModel = "BAAI/bge-small-en-v1.5",
            VectorDbProvider = "SqliteLocal",
            Collection = BrainStorage.CollectionName(projectRoot),
            VectorDimension = previous?.VectorDimension ?? 0,
            Status = ProjectBrainStatus.SemanticIndexing,
            QuickBrainStatus = ProjectBrainStatus.Ready,
            SemanticBrainStatus = ProjectBrainStatus.SemanticIndexing,
            DeepBrainStatus = ProjectBrainStatus.FolderSelected,
            TotalFiles = brain.Files.Count,
            IndexedFiles = brain.Files.Count,
            TotalChunks = snapshot.Chunks.Count,
            EmbeddedChunks = 0,
            FailedChunks = 0,
            SkippedFiles = snapshot.SkippedFiles,
            LastIndexedAt = DateTimeOffset.UtcNow,
            FileHashes = previous?.FileHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ChunkHashes = previous?.ChunkHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private void SaveIndexMetadata(ProjectIndexMetadata metadata)
    {
        try
        {
            File.WriteAllText(
                BrainStorage.ProjectIndexPath(_baseDirectory, metadata.ProjectRoot),
                JsonSerializer.Serialize(metadata, BrainJson.Options));
        }
        catch
        {
            // Metadata persistence must not crash indexing.
        }
    }

    private string BrainPath(string projectRoot) =>
        Path.Combine(BrainStorage.BrainDirectory(_baseDirectory), $"brain-{BrainStorage.ProjectKey(NormalizeProjectRoot(projectRoot))}.json");

    public string? SkippedReportPath(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        var path = BrainStorage.SkippedReportPath(_baseDirectory, projectRoot);
        return File.Exists(path) ? path : null;
    }

    public IReadOnlyList<SkippedFile> LoadSkippedReport(string? projectRoot)
    {
        var path = SkippedReportPath(projectRoot);
        if (path is null)
        {
            return Array.Empty<SkippedFile>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<SkippedFile>>(File.ReadAllText(path), BrainJson.Options) ?? new List<SkippedFile>();
        }
        catch
        {
            return Array.Empty<SkippedFile>();
        }
    }

    private void SaveSkippedReport(string projectRoot, IReadOnlyList<SkippedFile> skipped)
    {
        try
        {
            var ordered = skipped.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToList();
            File.WriteAllText(
                BrainStorage.SkippedReportPath(_baseDirectory, projectRoot),
                JsonSerializer.Serialize(ordered, BrainJson.Options));
        }
        catch
        {
            // Skipped report is diagnostic only; never fail indexing over it.
        }
    }

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

    private static int EstimateTokens(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (int)Math.Round(text.Length / 4.0));

    public void Dispose() => _ollama.Dispose();
}
