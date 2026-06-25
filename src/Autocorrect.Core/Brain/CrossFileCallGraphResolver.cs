namespace Autocorrect.Core.Brain;

// Resolves same-name symbols across files into cross-file call edges (graphify EXTRACTED style).
public static class CrossFileCallGraphResolver
{
    public static void Apply(ProjectBrainData brain)
    {
        if (brain.Graph.Nodes.Count == 0)
        {
            return;
        }

        var symbolNodes = brain.Graph.Nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.Path))
            .Where(node => node.Meta.GetValueOrDefault("kind") is not ("file" or "import" or "call" or "rationale"))
            .ToList();

        var index = symbolNodes
            .GroupBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var callNodes = brain.Graph.Nodes
            .Where(node => node.Meta.GetValueOrDefault("kind") == "call" || node.Id.Contains(":call:", StringComparison.Ordinal))
            .ToList();

        foreach (var callNode in callNodes)
        {
            if (!TryResolveCaller(brain, callNode.Id, out var callerId, out var callerPath))
            {
                continue;
            }

            if (!index.TryGetValue(callNode.Label, out var targets))
            {
                continue;
            }

            foreach (var target in targets.Where(target => !string.Equals(target.Path, callerPath, StringComparison.OrdinalIgnoreCase)))
            {
                brain.Graph.AddEdge(callerId, target.Id, EdgeType.Calls);
                if (!string.IsNullOrWhiteSpace(callerPath) && !string.IsNullOrWhiteSpace(target.Path))
                {
                    brain.Graph.AddEdge($"file:{callerPath}", $"file:{target.Path}", EdgeType.Calls);
                }
            }
        }

        foreach (var node in symbolNodes)
        {
            foreach (var edge in brain.Graph.Edges.Where(edge => edge.From == node.Id && edge.Type == EdgeType.Calls))
            {
                var target = brain.Graph.Nodes.FirstOrDefault(item => item.Id == edge.To);
                if (target is null || string.Equals(target.Path, node.Path, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(node.Path) && !string.IsNullOrWhiteSpace(target.Path))
                {
                    brain.Graph.AddEdge($"file:{node.Path}", $"file:{target.Path}", EdgeType.Calls);
                }
            }
        }
    }

    private static bool TryResolveCaller(ProjectBrainData brain, string callNodeId, out string callerId, out string? callerPath)
    {
        callerId = string.Empty;
        callerPath = null;
        var incoming = brain.Graph.Edges.FirstOrDefault(edge => edge.To == callNodeId && edge.Type == EdgeType.Calls);
        if (incoming is null)
        {
            return false;
        }

        callerId = incoming.From;
        var resolvedCallerId = callerId;
        callerPath = brain.Graph.Nodes.FirstOrDefault(node => node.Id == resolvedCallerId)?.Path;
        return !string.IsNullOrWhiteSpace(callerId);
    }
}
