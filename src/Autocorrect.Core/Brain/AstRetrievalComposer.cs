namespace Autocorrect.Core.Brain;

// Shared AST + tree-sitter graph retrieval used by --ast and hybrid modes.
public sealed class AstRetrievalComposer
{
    private readonly AstGraphRetriever _astRetriever = new();

    public async Task<List<RetrievalResult>> CollectAsync(
        string cleaned,
        ProjectBrainData brain,
        string collection,
        int topK,
        bool vectorsReady,
        IProjectVectorStore vectorStore,
        CancellationToken cancellationToken)
    {
        var parsed = PromptSymbolParser.Parse(cleaned);
        var terms = AstSearchTermExpander.Expand(cleaned, parsed);
        var results = _astRetriever.Retrieve(cleaned, brain, topK).ToList();

        if (vectorsReady)
        {
            if (parsed.Symbols.Count > 0 || terms.Count > 0)
            {
                var symbols = parsed.Symbols.Concat(terms).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList();
                results.AddRange(await vectorStore.SearchBySymbolsAsync(collection, symbols, 5, cancellationToken));
            }

            results.AddRange(await vectorStore.SearchByTermsAsync(collection, terms, 6, cancellationToken));

            var deepNeighbors = GraphRetriever.DeepNeighborFilePaths(brain, results, maxFiles: 18, maxHops: 2);
            if (deepNeighbors.Count > 0)
            {
                results.AddRange(await vectorStore.GetChunksByFilesAsync(collection, deepNeighbors, 5, cancellationToken));
            }
        }

        ArchitectureIntentBooster.Apply(results, cleaned, brain);
        ArchitectureProfileBooster.Apply(results, cleaned, brain);
        return results;
    }

    public static List<RetrievalResult> RankLineFirst(IReadOnlyList<RetrievalResult> results, int topK) =>
        results
            .Where(result => !string.IsNullOrWhiteSpace(result.FilePath))
            .GroupBy(result => string.IsNullOrWhiteSpace(result.Symbol) ? result.FilePath : $"{result.FilePath}:{result.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .Where(result => result.StartLine > 0 || result.Score >= 0.5)
            .OrderByDescending(result => result.StartLine > 0 ? 1 : 0)
            .ThenByDescending(result => result.Score)
            .Take(Math.Max(topK, 14))
            .ToList();
}
