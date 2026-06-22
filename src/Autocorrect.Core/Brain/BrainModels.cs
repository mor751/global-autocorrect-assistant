using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autocorrect.Core.Brain;

public enum FileRole
{
    Route,
    Component,
    Hook,
    Util,
    Api,
    Config,
    Style,
    Database,
    Docs,
    Test,
    Unknown
}

public enum NodeType
{
    Project,
    Folder,
    File,
    Route,
    Component,
    Function,
    Hook,
    Api,
    Style,
    Config,
    UserPreference,
    PromptHistory
}

public enum EdgeType
{
    Contains,
    Imports,
    Exports,
    Renders,
    Calls,
    DependsOn,
    RelatedTo,
    UsuallyEditedWith
}

public enum ProjectBrainStatus
{
    NoFolder,
    FolderSelected,
    QuickIndexing,
    SemanticIndexing,
    Ready,
    PartialReady,
    Error,
    VectorStoreUnavailable,
    EmbeddingUnavailable,
    ReindexRequired
}

public enum RetrievalMode
{
    SemanticVector,
    HybridVectorKeyword,
    KeywordFallback,
    NoBrain
}

public sealed class ProjectFileSummary
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public FileRole Role { get; set; } = FileRole.Unknown;
    public List<string> Imports { get; set; } = new();
    public List<string> Exports { get; set; } = new();
    public List<string> Symbols { get; set; } = new();
    public string DetectedRole { get; set; } = string.Empty;
    public double Importance { get; set; }
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset LastModified { get; set; }
    public long SizeBytes { get; set; }
    public List<string> PreviewChunks { get; set; } = new();
}

public sealed class ProjectChunk
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string ChunkType { get; set; } = "text";
    public string Symbol { get; set; } = string.Empty;
    public string ParentSymbol { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentPreview { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
    public List<string> Imports { get; set; } = new();
    public List<string> Exports { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string FileHash { get; set; } = string.Empty;
    public string ChunkHash { get; set; } = string.Empty;
    public double Importance { get; set; }
    public DateTimeOffset IndexedAt { get; set; }

    public string EmbeddedText()
    {
        var symbol = string.IsNullOrWhiteSpace(Symbol) ? "none" : Symbol;
        var parent = string.IsNullOrWhiteSpace(ParentSymbol) ? "none" : ParentSymbol;
        var imports = Imports.Count == 0 ? "none" : string.Join(", ", Imports.Take(12));
        return $"File: {FilePath}\nLanguage: {Language}\nType: {ChunkType}\nSymbol: {symbol}\nParent: {parent}\nImports: {imports}\n\nCode/Text:\n{Content}";
    }
}

public sealed class SkippedFile
{
    public string Path { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class ProjectIndexSnapshot
{
    public ProjectBrainData Brain { get; set; } = new();
    public List<ProjectChunk> Chunks { get; set; } = new();
    public int SkippedFiles { get; set; }
    public List<string> SkippedReasons { get; set; } = new();
    public List<SkippedFile> SkippedDetails { get; set; } = new();
}

public sealed class ProjectIndexMetadata
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string Framework { get; set; } = "unknown";
    public string EmbeddingProvider { get; set; } = "LocalOnnx";
    public string EmbeddingModel { get; set; } = "BAAI/bge-small-en-v1.5";
    public int VectorDimension { get; set; }
    public string VectorDbProvider { get; set; } = "SqliteLocal";
    public string Collection { get; set; } = string.Empty;
    public ProjectBrainStatus Status { get; set; } = ProjectBrainStatus.NoFolder;
    public ProjectBrainStatus QuickBrainStatus { get; set; } = ProjectBrainStatus.NoFolder;
    public ProjectBrainStatus SemanticBrainStatus { get; set; } = ProjectBrainStatus.NoFolder;
    public ProjectBrainStatus DeepBrainStatus { get; set; } = ProjectBrainStatus.FolderSelected;
    public int TotalFiles { get; set; }
    public int IndexedFiles { get; set; }
    public int TotalChunks { get; set; }
    public int EmbeddedChunks { get; set; }
    public int FailedChunks { get; set; }
    public int SkippedFiles { get; set; }
    public DateTimeOffset? LastIndexedAt { get; set; }
    public string? LastError { get; set; }
    public Dictionary<string, string> FileHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ChunkHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentRagMode()
    {
        return Status switch
        {
            ProjectBrainStatus.Ready => "Semantic + keyword",
            ProjectBrainStatus.PartialReady => "Partial semantic + keyword",
            ProjectBrainStatus.VectorStoreUnavailable => "Keyword fallback",
            ProjectBrainStatus.EmbeddingUnavailable => "Keyword fallback",
            _ => "No brain"
        };
    }
}

public sealed class RetrievalResult
{
    public double Score { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ChunkType { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ContentPreview { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RetrievalResponse
{
    public string Query { get; set; } = string.Empty;
    public RetrievalMode RetrievalMode { get; set; } = RetrievalMode.NoBrain;
    public List<RetrievalResult> Results { get; set; } = new();
}

public enum PromptTargetAgent
{
    Generic,
    Cursor,
    Codex,
    ClaudeCode
}

public sealed class PromptCompilerRequest
{
    public string OriginalPrompt { get; set; } = string.Empty;
    public PromptTargetAgent TargetAgent { get; set; } = PromptTargetAgent.Codex;
    public ProjectBrainData? Brain { get; set; }
    public RetrievalResponse Retrieval { get; set; } = new();
    public IReadOnlyList<string> UserConstraints { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingContext { get; set; } = Array.Empty<string>();
}

public sealed class PromptCompilerResult
{
    public string OptimizedPrompt { get; set; } = string.Empty;
    public List<string> RelevantFiles { get; set; } = new();
    public List<string> Tasks { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public string TokenSavingReason { get; set; } = string.Empty;
    public string RetrievalSummary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public bool UsedWriterModel { get; set; }
}

public sealed class ProjectStack
{
    public string? Framework { get; set; }
    public string? Language { get; set; }
    public string? Styling { get; set; }
    public string? UiLibrary { get; set; }
    public string? PackageManager { get; set; }

    public IEnumerable<string> Describe()
    {
        if (!string.IsNullOrWhiteSpace(Framework)) yield return Framework!;
        if (!string.IsNullOrWhiteSpace(Language)) yield return Language!;
        if (!string.IsNullOrWhiteSpace(Styling)) yield return Styling!;
        if (!string.IsNullOrWhiteSpace(UiLibrary)) yield return UiLibrary!;
        if (!string.IsNullOrWhiteSpace(PackageManager)) yield return PackageManager!;
    }
}

public sealed class ProjectFolders
{
    public List<string> Routes { get; set; } = new();
    public List<string> Components { get; set; } = new();
    public List<string> Utils { get; set; } = new();
    public List<string> Styles { get; set; } = new();
    public List<string> Api { get; set; } = new();
    public List<string> Data { get; set; } = new();
}

public sealed class GraphNode
{
    public string Id { get; set; } = string.Empty;
    public NodeType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Path { get; set; }
    public Dictionary<string, string> Meta { get; set; } = new();
}

public sealed class GraphEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public EdgeType Type { get; set; }
}

public sealed class ProjectGraph
{
    public List<GraphNode> Nodes { get; set; } = new();
    public List<GraphEdge> Edges { get; set; } = new();

    // Adds a node only once per id and returns its id for edge wiring.
    public string AddNode(string id, NodeType type, string label, string? path = null)
    {
        if (Nodes.All(n => n.Id != id))
        {
            Nodes.Add(new GraphNode { Id = id, Type = type, Label = label, Path = path });
        }

        return id;
    }

    // Adds a directed edge once, ignoring self-loops and duplicates.
    public void AddEdge(string from, string to, EdgeType type)
    {
        if (from == to || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
        {
            return;
        }

        if (Edges.Any(e => e.From == from && e.To == to && e.Type == type))
        {
            return;
        }

        Edges.Add(new GraphEdge { From = from, To = to, Type = type });
    }
}

public sealed class ProjectBrainData
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public ProjectStack Stack { get; set; } = new();
    public ProjectFolders Folders { get; set; } = new();
    public List<string> Rules { get; set; } = new();
    public List<ProjectFileSummary> Files { get; set; } = new();
    public ProjectGraph Graph { get; set; } = new();
    public DateTimeOffset IndexedAt { get; set; }
}

// Central JSON configuration so every Brain store reads/writes consistently.
public static class BrainJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
