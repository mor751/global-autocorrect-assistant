namespace Autocorrect.Core.Brain;

// Resolves parsed prompt symbols to concrete files/symbols using the indexed brain + SQLite symbol index.
public static class SymbolTargetResolver
{
    public static IReadOnlyList<SymbolTarget> Resolve(PromptSymbolParseResult parsed, ProjectBrainData brain)
    {
        var targets = new List<SymbolTarget>();

        foreach (var filePath in parsed.FilePaths)
        {
            var file = brain.Files.FirstOrDefault(item =>
                item.Path.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
                item.Path.EndsWith(filePath, StringComparison.OrdinalIgnoreCase));
            if (file is not null)
            {
                targets.Add(new SymbolTarget(file.Path, string.Empty, 1.0, "prompt file path"));
            }
        }

        foreach (var symbol in parsed.Symbols)
        {
            var fileMatches = brain.Files
                .Where(file => file.Symbols.Any(item => item.Equals(symbol, StringComparison.OrdinalIgnoreCase)) ||
                               file.Path.Contains(symbol, StringComparison.OrdinalIgnoreCase))
                .Take(4)
                .ToList();

            foreach (var file in fileMatches)
            {
                targets.Add(new SymbolTarget(file.Path, symbol, 0.95, "prompt symbol match"));
            }

            var graphMatches = brain.Graph.Nodes
                .Where(node => node.Label.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrWhiteSpace(node.Path))
                .Take(4);

            foreach (var node in graphMatches)
            {
                targets.Add(new SymbolTarget(node.Path!, symbol, 0.98, "graph symbol match"));
            }
        }

        return targets
            .GroupBy(target => $"{target.FilePath}:{target.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .Take(12)
            .ToList();
    }
}

public sealed record SymbolTarget(string FilePath, string Symbol, double Score, string Reason);
