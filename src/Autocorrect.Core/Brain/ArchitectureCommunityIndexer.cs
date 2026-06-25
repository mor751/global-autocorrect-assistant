namespace Autocorrect.Core.Brain;

// Groups related files into architecture communities using import/call connectivity.
public static class ArchitectureCommunityIndexer
{
    public static List<ArchitectureCommunity> Build(ProjectBrainData brain)
    {
        var filePaths = brain.Files
            .Select(file => file.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (filePaths.Count == 0)
        {
            return new List<ArchitectureCommunity>();
        }

        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in filePaths)
        {
            adjacency[path] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var edge in brain.Graph.Edges.Where(edge => edge.Type is EdgeType.Imports or EdgeType.Calls or EdgeType.DependsOn))
        {
            var from = FilePathForNode(brain, edge.From);
            var to = FilePathForNode(brain, edge.To);
            if (from is null || to is null || !filePaths.Contains(from) || !filePaths.Contains(to))
            {
                continue;
            }

            adjacency[from].Add(to);
            adjacency[to].Add(from);
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var communities = new List<ArchitectureCommunity>();
        var roleByPath = brain.Files.ToDictionary(file => file.Path, file => file.Role, StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var path in filePaths.OrderByDescending(path => brain.Files.First(file => file.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).Importance))
        {
            if (!visited.Add(path))
            {
                continue;
            }

            var members = CollectComponent(path, adjacency, visited);
            if (members.Count == 0)
            {
                continue;
            }

            var dominant = members
                .GroupBy(member => TopFolder(member), StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .First()
                .Key;

            var layer = DominantLayer(members, roleByPath);
            communities.Add(new ArchitectureCommunity
            {
                Id = $"community:{index++}",
                Name = dominant,
                Layer = layer,
                FilePaths = members.OrderBy(member => member, StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        return communities
            .OrderByDescending(community => community.FilePaths.Count)
            .Take(24)
            .ToList();
    }

    private static List<string> CollectComponent(string start, IReadOnlyDictionary<string, HashSet<string>> adjacency, HashSet<string> visited)
    {
        var members = new List<string> { start };
        var queue = new Queue<string>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current, out var neighbors))
            {
                continue;
            }

            foreach (var neighbor in neighbors)
            {
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                members.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return members;
    }

    private static string? FilePathForNode(ProjectBrainData brain, string nodeId)
    {
        if (nodeId.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return nodeId["file:".Length..];
        }

        return brain.Graph.Nodes.FirstOrDefault(node => node.Id == nodeId)?.Path;
    }

    private static string TopFolder(string path)
    {
        var normalized = path.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return $"{parts[0]}/{parts[1]}";
        }

        return parts.Length == 1 ? parts[0] : "root";
    }

    private static string DominantLayer(IReadOnlyList<string> members, IReadOnlyDictionary<string, FileRole> roleByPath)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (!roleByPath.TryGetValue(member, out var role))
            {
                continue;
            }

            var layer = role switch
            {
                FileRole.Config => "config",
                FileRole.Api or FileRole.Route => "api",
                FileRole.Component or FileRole.Style => "ui",
                FileRole.Database => "data",
                _ => "core"
            };
            counts[layer] = counts.GetValueOrDefault(layer) + 1;
        }

        return counts.Count == 0
            ? "core"
            : counts.OrderByDescending(pair => pair.Value).First().Key;
    }
}
