namespace Autocorrect.Core.Brain;

public sealed class AstGraphRetriever
{
    public IReadOnlyList<RetrievalResult> Retrieve(
        string query,
        ProjectBrainData brain,
        int topK)
    {
        var cleaned = CleanQuery(query);
        var parsed = PromptSymbolParser.Parse(cleaned);
        var terms = AstSearchTermExpander.Expand(cleaned, parsed);
        var symbolTargets = SymbolTargetResolver.Resolve(parsed, brain);
        var seeds = symbolTargets
            .Select(target => new RetrievalResult
            {
                FilePath = target.FilePath,
                Symbol = target.Symbol,
                Score = target.Score,
                Reason = target.Reason,
                ChunkType = "symbol",
                ContentPreview = target.Symbol
            })
            .ToList();

        seeds.AddRange(MatchGraphNodes(terms, brain));
        seeds.AddRange(ResolveTermSeeds(terms, brain));

        if (seeds.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        seeds.AddRange(GraphSymbolTraverser.Traverse(brain, seeds));
        ArchitectureIntentBooster.Apply(seeds, cleaned, brain);

        var neighborPaths = GraphRetriever.DeepNeighborFilePaths(brain, seeds, maxFiles: Math.Max(topK, 16));
        foreach (var path in neighborPaths)
        {
            if (seeds.Any(seed => seed.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            seeds.Add(new RetrievalResult
            {
                FilePath = path,
                Score = seeds.Max(item => item.Score) * 0.82,
                Reason = "AST graph neighbor",
                ChunkType = "graph",
                ContentPreview = path
            });
        }

        return Rank(seeds, topK * 3);
    }

    private static IEnumerable<RetrievalResult> MatchGraphNodes(IReadOnlyList<string> terms, ProjectBrainData brain)
    {
        foreach (var node in brain.Graph.Nodes.Where(node => !string.IsNullOrWhiteSpace(node.Path)))
        {
            var label = node.Label ?? string.Empty;
            var path = node.Path!;
            if (!terms.Any(term => label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                                   path.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var startLine = node.Meta.TryGetValue("startLine", out var raw) && int.TryParse(raw, out var parsed) ? parsed : 0;
            var endLine = node.Meta.TryGetValue("endLine", out var rawEnd) && int.TryParse(rawEnd, out var parsedEnd) ? parsedEnd : startLine;
            yield return new RetrievalResult
            {
                FilePath = path,
                Symbol = label,
                Score = 0.97,
                Reason = "AST graph symbol",
                ChunkType = node.Type.ToString().ToLowerInvariant(),
                StartLine = startLine,
                EndLine = endLine > 0 ? endLine : startLine,
                ContentPreview = label
            };
        }
    }

    private static IEnumerable<RetrievalResult> ResolveTermSeeds(IReadOnlyList<string> terms, ProjectBrainData brain)
    {
        foreach (var file in brain.Files)
        {
            var score = 0.0;
            foreach (var term in terms)
            {
                if (file.Path.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.25;
                }

                if (file.Symbols.Any(symbol => symbol.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 0.4;
                }

                if (file.Summary.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    score += 0.1;
                }
            }

            if (score <= 0)
            {
                continue;
            }

            yield return new RetrievalResult
            {
                FilePath = file.Path,
                Score = Math.Min(0.95, 0.4 + score),
                Reason = "AST file/symbol match",
                ChunkType = "file",
                ContentPreview = file.Summary
            };
        }
    }

    private static List<RetrievalResult> Rank(IReadOnlyList<RetrievalResult> seeds, int topK) =>
        seeds
            .GroupBy(result => string.IsNullOrWhiteSpace(result.Symbol) ? result.FilePath : $"{result.FilePath}:{result.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Score).First())
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();

    private static string CleanQuery(string query) =>
        string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
