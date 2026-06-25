namespace Autocorrect.Core.Brain;

public sealed class ArchitectureHub
{
    public string FilePath { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Layer { get; set; } = "core";
    public int ConnectionCount { get; set; }
}

public sealed class ArchitectureProfile
{
    public List<ArchitectureHub> Hubs { get; set; } = new();
    public List<ArchitectureEntryPoint> EntryPoints { get; set; } = new();
    public List<ArchitectureCommunity> Communities { get; set; } = new();
    public Dictionary<string, int> LayerCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

// Builds a deterministic architecture profile from the tree-sitter graph at index time.
public static class ProjectArchitectureIndexer
{
    private const int MaxHubs = 24;

    public static ArchitectureProfile Build(ProjectBrainData brain)
    {
        var profile = new ArchitectureProfile();
        if (brain.Graph.Nodes.Count == 0)
        {
            return profile;
        }

        var roleByPath = brain.Files.ToDictionary(file => file.Path, file => file.Role, StringComparer.OrdinalIgnoreCase);
        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var edge in brain.Graph.Edges)
        {
            Increment(degree, edge.From);
            Increment(degree, edge.To);
        }

        foreach (var node in brain.Graph.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.Path)))
        {
            var path = node.Path!;
            if (!degree.TryGetValue(node.Id, out var connections) || connections < 2)
            {
                continue;
            }

            roleByPath.TryGetValue(path, out var role);
            var layer = LayerFor(role, path);
            profile.Hubs.Add(new ArchitectureHub
            {
                FilePath = path,
                Label = node.Label,
                Layer = layer,
                ConnectionCount = connections
            });
            profile.LayerCounts[layer] = profile.LayerCounts.GetValueOrDefault(layer) + 1;
        }

        profile.Hubs = profile.Hubs
            .GroupBy(hub => hub.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(hub => hub.ConnectionCount).First())
            .OrderByDescending(hub => hub.ConnectionCount)
            .Take(MaxHubs)
            .ToList();

        profile.EntryPoints = EntryPointIndexer.Detect(brain);
        profile.Communities = ArchitectureCommunityIndexer.Build(brain);
        return profile;
    }

    private static string LayerFor(FileRole role, string path)
    {
        var lower = path.ToLowerInvariant();
        if (role is FileRole.Config || lower.Contains("appsettings") || lower.Contains("config"))
        {
            return "config";
        }

        if (role is FileRole.Api or FileRole.Route || lower.Contains("/api/") || lower.Contains("controller"))
        {
            return "api";
        }

        if (role is FileRole.Component or FileRole.Style || lower.EndsWith(".xaml") || lower.Contains("/ui/"))
        {
            return "ui";
        }

        if (role is FileRole.Database || lower.Contains("migration") || lower.Contains("repository"))
        {
            return "data";
        }

        return "core";
    }

    private static void Increment(Dictionary<string, int> degree, string nodeId)
    {
        degree[nodeId] = degree.GetValueOrDefault(nodeId) + 1;
    }
}
