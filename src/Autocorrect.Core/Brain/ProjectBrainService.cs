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
    public int RetrievalTopK { get; set; } = 12;
    public string QdrantUrl { get; set; } = "http://localhost:6333";
    public string VectorDbProvider { get; set; } = "QdrantLocal";
    public string EmbeddingProvider { get; set; } = "FastEmbed";
    public string EmbeddingModel { get; set; } = "BAAI/bge-small-en-v1.5";
    public string FastEmbedSidecarUrl { get; set; } = "http://127.0.0.1:8765";
    public string PythonExecutable { get; set; } = "python";
    public int EmbeddingBatchSize { get; set; } = 32;
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
        var snapshot = await Task.Run(() =>
        {
            return _indexer is ProjectIndexer concrete
                ? concrete.IndexDetailed(projectRoot, _options.Index)
                : new ProjectIndexSnapshot { Brain = _indexer.Index(projectRoot, _options.Index) };
        }, cancellationToken);
        var brain = snapshot.Brain;
        SaveBrain(brain);

        var fallbackStore = CreateFallbackStore(projectRoot);
        await fallbackStore.ClearProjectAsync(projectRoot, cancellationToken);
        await fallbackStore.AddDocumentsAsync(brain.Files.Select(ToDocument), cancellationToken);

        var previous = LoadIndexMetadata(projectRoot);
        var metadata = CreateMetadata(projectRoot, brain, snapshot, previous);
        metadata.Status = ProjectBrainStatus.SemanticIndexing;
        metadata.QuickBrainStatus = ProjectBrainStatus.Ready;
        SaveIndexMetadata(metadata);

        using var embeddings = CreateFastEmbedService();
        using var qdrant = new QdrantVectorStore(_options.QdrantUrl);

        if (!await embeddings.IsAvailableAsync(cancellationToken))
        {
            metadata.Status = ProjectBrainStatus.EmbeddingUnavailable;
            metadata.SemanticBrainStatus = ProjectBrainStatus.EmbeddingUnavailable;
            metadata.LastError = "FastEmbed unavailable. Install Python and run: pip install fastembed";
            SaveIndexMetadata(metadata);
            return brain;
        }

        var dimension = embeddings.GetVectorDimension();
        metadata.VectorDimension = dimension;
        if (!await qdrant.EnsureCollectionAsync(metadata.QdrantCollection, dimension, cancellationToken))
        {
            metadata.Status = ProjectBrainStatus.QdrantUnavailable;
            metadata.SemanticBrainStatus = ProjectBrainStatus.QdrantUnavailable;
            metadata.LastError = $"Qdrant unavailable at {_options.QdrantUrl}";
            SaveIndexMetadata(metadata);
            return brain;
        }

        var previousFiles = previous?.FileHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var previousChunks = previous?.ChunkHashes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var currentFiles = snapshot.Chunks.Select(chunk => chunk.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var deleted in previousFiles.Keys.Where(path => !currentFiles.Contains(path)).ToList())
        {
            await qdrant.DeleteFileAsync(metadata.QdrantCollection, deleted, cancellationToken);
        }

        var chunksToEmbed = snapshot.Chunks
            .Where(chunk =>
                !previousFiles.TryGetValue(chunk.FilePath, out var oldFileHash) ||
                !oldFileHash.Equals(chunk.FileHash, StringComparison.OrdinalIgnoreCase) ||
                !previousChunks.TryGetValue(chunk.Id, out var oldChunkHash) ||
                !oldChunkHash.Equals(chunk.ChunkHash, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var batch in chunksToEmbed.Chunk(Math.Clamp(_options.EmbeddingBatchSize, 1, 128)))
        {
            var vectors = await embeddings.EmbedBatchAsync(batch.Select(chunk => $"passage: {chunk.EmbeddedText()}"), cancellationToken);
            if (vectors.Count != batch.Length)
            {
                metadata.FailedChunks += batch.Length;
                continue;
            }

            await qdrant.UpsertAsync(metadata.QdrantCollection, batch, vectors, cancellationToken);
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

    public async Task<EnhancementOutcome> EnhanceAsync(string originalPrompt, string? projectRoot, CancellationToken cancellationToken)
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
            TargetAgent = PromptTargetAgent.Codex,
            Brain = brain,
            Retrieval = retrieval
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
        using var embeddings = CreateFastEmbedService();
        using var qdrant = new QdrantVectorStore(_options.QdrantUrl);
        var fallback = CreateFallbackStore(projectRoot);
        var retrieval = new RagRetrievalService(embeddings, qdrant, fallback, metadata);
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
        using var qdrant = new QdrantVectorStore(_options.QdrantUrl);
        return await qdrant.GetStatsAsync(metadata?.QdrantCollection ?? BrainStorage.CollectionName(projectRoot), cancellationToken);
    }

    public async Task<FastEmbedDiagnostics> TestFastEmbedAsync(CancellationToken cancellationToken)
    {
        using var embeddings = CreateFastEmbedService();
        return await embeddings.RunDiagnosticsAsync(loadModel: true, testEmbedding: true, cancellationToken);
    }

    private FastEmbedEmbeddingService CreateFastEmbedService() =>
        new(
            _options.EmbeddingModel,
            _options.EmbeddingBatchSize,
            _options.FastEmbedSidecarUrl,
            _options.PythonExecutable);

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
            EmbeddingProvider = _options.EmbeddingProvider,
            EmbeddingModel = _options.EmbeddingModel,
            VectorDbProvider = _options.VectorDbProvider,
            QdrantUrl = _options.QdrantUrl,
            QdrantCollection = BrainStorage.CollectionName(projectRoot),
            VectorDimension = previous?.EmbeddingModel == _options.EmbeddingModel ? previous.VectorDimension : 0,
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
