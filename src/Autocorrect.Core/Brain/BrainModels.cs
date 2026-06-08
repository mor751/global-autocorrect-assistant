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

public sealed class ProjectFileSummary
{
    public string Path { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public FileRole Role { get; set; } = FileRole.Unknown;
    public List<string> Imports { get; set; } = new();
    public List<string> Exports { get; set; } = new();
    public List<string> Symbols { get; set; } = new();
    public DateTimeOffset LastModified { get; set; }
    public long SizeBytes { get; set; }
    public List<string> PreviewChunks { get; set; } = new();
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
