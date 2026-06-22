namespace Autocorrect.Core.Brain;

// Expands vector hits along import/call graph edges so related files surface together.
public static class GraphRetriever
{
    public static IReadOnlyList<string> NeighborFilePaths(ProjectBrainData brain, IReadOnlyList<RetrievalResult> seeds, int maxFiles = 8)
    {
        if (seeds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seedFiles = seeds
            .Select(result => result.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var neighbors = new List<string>();
        foreach (var edge in brain.Graph.Edges)
        {
            if (edge.Type is not (EdgeType.Imports or EdgeType.Calls or EdgeType.DependsOn))
            {
                continue;
            }

            var fromFile = NodeFilePath(edge.From);
            var toFile = NodeFilePath(edge.To);
            if (fromFile is null || toFile is null)
            {
                continue;
            }

            if (seedFiles.Contains(fromFile) && !seedFiles.Contains(toFile))
            {
                neighbors.Add(toFile);
            }
            else if (seedFiles.Contains(toFile) && !seedFiles.Contains(fromFile))
            {
                neighbors.Add(fromFile);
            }

            if (neighbors.Count >= maxFiles)
            {
                break;
            }
        }

        return neighbors
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maxFiles)
            .ToList();
    }

    public static List<RetrievalResult> MergeGraphNeighbors(
        IReadOnlyList<RetrievalResult> ranked,
        IReadOnlyList<RetrievalResult> neighbors,
        int topK)
    {
        if (neighbors.Count == 0)
        {
            return ranked.Take(topK).ToList();
        }

        var maxSeed = ranked.Count > 0 ? ranked.Max(result => result.Score) : 0.5;
        var neighborBoost = Math.Max(0.35, maxSeed * 0.72);
        var merged = ranked.ToList();
        foreach (var neighbor in neighbors)
        {
            neighbor.Score = Math.Max(neighbor.Score, neighborBoost);
            neighbor.Reason = string.IsNullOrWhiteSpace(neighbor.Reason) ? "graph neighbor" : $"{neighbor.Reason} + graph neighbor";
            merged.Add(neighbor);
        }

        return merged
            .GroupBy(result => string.IsNullOrWhiteSpace(result.Symbol) ? result.FilePath : $"{result.FilePath}:{result.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Score).First())
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    private static string? NodeFilePath(string nodeId)
    {
        if (!nodeId.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return nodeId["file:".Length..];
    }
}
