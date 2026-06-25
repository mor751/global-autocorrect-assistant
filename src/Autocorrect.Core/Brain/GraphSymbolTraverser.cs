namespace Autocorrect.Core.Brain;

// Graphify-style BFS over symbol/call/import edges to surface architecture neighbors.
public static class GraphSymbolTraverser
{
    private static readonly EdgeType[] TraversalEdges =
    [
        EdgeType.Calls,
        EdgeType.Imports,
        EdgeType.Contains,
        EdgeType.Exports,
        EdgeType.DependsOn,
        EdgeType.RelatedTo
    ];

    public static IReadOnlyList<RetrievalResult> Traverse(
        ProjectBrainData brain,
        IReadOnlyList<RetrievalResult> seeds,
        int maxHops = 3,
        int maxResults = 18)
    {
        if (seeds.Count == 0 || brain.Graph.Nodes.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        var nodeById = brain.Graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var adjacency = BuildAdjacency(brain.Graph.Edges);
        var startIds = ResolveSeedNodeIds(brain, seeds);
        if (startIds.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<(string Id, int Hop)>();
        foreach (var id in startIds)
        {
            visited.Add(id);
            frontier.Enqueue((id, 0));
        }

        var results = new List<RetrievalResult>();
        while (frontier.Count > 0 && results.Count < maxResults)
        {
            var (currentId, hop) = frontier.Dequeue();
            if (!nodeById.TryGetValue(currentId, out var current))
            {
                continue;
            }

            if (hop > 0)
            {
                results.Add(ToRetrievalResult(current, Math.Max(0.55, 0.92 - hop * 0.12), $"AST graph hop {hop}"));
            }

            if (hop >= maxHops)
            {
                continue;
            }

            if (!adjacency.TryGetValue(currentId, out var neighbors))
            {
                continue;
            }

            foreach (var neighborId in neighbors)
            {
                if (!visited.Add(neighborId))
                {
                    continue;
                }

                frontier.Enqueue((neighborId, hop + 1));
            }
        }

        return results;
    }

    private static Dictionary<string, List<string>> BuildAdjacency(IReadOnlyList<GraphEdge> edges)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!TraversalEdges.Contains(edge.Type))
            {
                continue;
            }

            AddNeighbor(adjacency, edge.From, edge.To);
            AddNeighbor(adjacency, edge.To, edge.From);
        }

        return adjacency;
    }

    private static void AddNeighbor(Dictionary<string, List<string>> adjacency, string from, string to)
    {
        if (!adjacency.TryGetValue(from, out var list))
        {
            list = new List<string>();
            adjacency[from] = list;
        }

        if (!list.Contains(to, StringComparer.Ordinal))
        {
            list.Add(to);
        }
    }

    private static HashSet<string> ResolveSeedNodeIds(ProjectBrainData brain, IReadOnlyList<RetrievalResult> seeds)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            if (string.IsNullOrWhiteSpace(seed.FilePath))
            {
                continue;
            }

            ids.Add($"file:{seed.FilePath}");

            foreach (var node in brain.Graph.Nodes)
            {
                if (!string.Equals(node.Path, seed.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(seed.Symbol) ||
                    node.Label.Equals(seed.Symbol, StringComparison.OrdinalIgnoreCase) ||
                    node.Label.Contains(seed.Symbol, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(node.Id);
                }
            }
        }

        return ids;
    }

    private static RetrievalResult ToRetrievalResult(GraphNode node, double score, string reason)
    {
        var startLine = node.Meta.TryGetValue("startLine", out var rawStart) && int.TryParse(rawStart, out var parsedStart) ? parsedStart : 0;
        var endLine = node.Meta.TryGetValue("endLine", out var rawEnd) && int.TryParse(rawEnd, out var parsedEnd) ? parsedEnd : startLine;
        return new RetrievalResult
        {
            FilePath = node.Path ?? string.Empty,
            Symbol = node.Label,
            Score = score,
            Reason = reason,
            ChunkType = node.Type.ToString().ToLowerInvariant(),
            StartLine = startLine,
            EndLine = endLine > 0 ? endLine : startLine,
            ContentPreview = node.Label
        };
    }
}
