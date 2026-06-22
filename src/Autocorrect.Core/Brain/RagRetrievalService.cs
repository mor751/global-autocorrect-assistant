namespace Autocorrect.Core.Brain;

public sealed class RagRetrievalService
{
    private readonly IEmbeddingService _embeddings;
    private readonly IProjectVectorStore _vectorStore;
    private readonly FileVectorStore _fallbackStore;
    private readonly ProjectIndexMetadata? _metadata;

    public RagRetrievalService(
        IEmbeddingService embeddings,
        IProjectVectorStore vectorStore,
        FileVectorStore fallbackStore,
        ProjectIndexMetadata? metadata)
    {
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _fallbackStore = fallbackStore;
        _metadata = metadata;
    }

    public async Task<RetrievalResponse> RetrieveAsync(
        string query,
        ProjectBrainData? brain,
        string projectRoot,
        int topK,
        CancellationToken cancellationToken)
    {
        if (brain is null || string.IsNullOrWhiteSpace(query))
        {
            return new RetrievalResponse { Query = query, RetrievalMode = RetrievalMode.NoBrain };
        }

        var cleaned = CleanQuery(query);
        var collection = _metadata?.Collection ?? BrainStorage.CollectionName(projectRoot);
        var parsed = PromptSymbolParser.Parse(cleaned);
        var symbolTargets = SymbolTargetResolver.Resolve(parsed, brain);

        if (_metadata?.Status is ProjectBrainStatus.Ready or ProjectBrainStatus.PartialReady)
        {
            var symbolHits = new List<RetrievalResult>();
            if (parsed.Symbols.Count > 0)
            {
                symbolHits.AddRange(await _vectorStore.SearchBySymbolsAsync(collection, parsed.Symbols, 3, cancellationToken));
            }

            if (symbolTargets.Count > 0)
            {
                var targetFiles = symbolTargets.Select(target => target.FilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                symbolHits.AddRange(await _vectorStore.GetChunksByFilesAsync(collection, targetFiles, 2, cancellationToken));
                foreach (var hit in symbolHits.Where(hit => hit.Score <= 0))
                {
                    hit.Score = 0.96;
                    hit.Reason = "prompt symbol target";
                }
            }

            var queryVector = await _embeddings.EmbedTextAsync($"query: {cleaned}", cancellationToken);
            if (queryVector is { Length: > 0 })
            {
                var semantic = await _vectorStore.SearchAsync(collection, queryVector, Math.Max(topK * 2, topK), cancellationToken);
                var merged = symbolHits
                    .Concat(semantic)
                    .ToList();
                var boosted = BoostAndDedupe(cleaned, merged, topK * 2);
                if (boosted.Count > 0)
                {
                    var neighborPaths = GraphRetriever.NeighborFilePaths(brain, boosted);
                    if (neighborPaths.Count > 0)
                    {
                        var neighborChunks = await _vectorStore.GetChunksByFilesAsync(collection, neighborPaths, 2, cancellationToken);
                        boosted = GraphRetriever.MergeGraphNeighbors(boosted, neighborChunks, topK);
                    }
                    else
                    {
                        boosted = boosted.Take(topK).ToList();
                    }

                    return new RetrievalResponse
                    {
                        Query = cleaned,
                        RetrievalMode = RetrievalMode.HybridVectorKeyword,
                        Results = boosted
                    };
                }
            }
        }

        var fallback = await _fallbackStore.SearchAsync(cleaned, topK, cancellationToken);
        return new RetrievalResponse
        {
            Query = cleaned,
            RetrievalMode = RetrievalMode.KeywordFallback,
            Results = fallback.Select(hit => new RetrievalResult
            {
                Score = hit.Score,
                FilePath = hit.Document.Path,
                ChunkType = hit.Document.Role.ToString().ToLowerInvariant(),
                Reason = hit.Reason,
                ContentPreview = hit.Document.Summary,
                Content = hit.Document.Text,
                Metadata = new Dictionary<string, string>
                {
                    ["role"] = hit.Document.Role.ToString()
                }
            }).ToList()
        };
    }

    private static List<RetrievalResult> BoostAndDedupe(string query, IReadOnlyList<RetrievalResult> results, int topK)
    {
        var terms = Tokenize(query);
        return results
            .Select(result =>
            {
                var score = result.Score;
                var haystack = $"{result.FilePath} {result.Symbol} {result.ChunkType} {result.ContentPreview}".ToLowerInvariant();
                foreach (var term in terms)
                {
                    if (result.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 0.12;
                    if (!string.IsNullOrWhiteSpace(result.Symbol) && result.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 0.1;
                    if (haystack.Contains(term, StringComparison.Ordinal)) score += 0.04;
                }

                if (result.Metadata.TryGetValue("importance", out var raw) && double.TryParse(raw, out var importance))
                {
                    score += importance * 0.06;
                }

                if ((result.FilePath.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                     result.FilePath.Contains("spec", StringComparison.OrdinalIgnoreCase)) &&
                    !terms.Contains("test") &&
                    !terms.Contains("spec"))
                {
                    score -= 0.12;
                }

                result.Score = score;
                result.Reason = ReasonFor(result, terms);
                return result;
            })
            .Where(result => result.Score > 0)
            .GroupBy(result => string.IsNullOrWhiteSpace(result.Symbol) ? result.FilePath : $"{result.FilePath}:{result.Symbol}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.Score).First())
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    private static string ReasonFor(RetrievalResult result, HashSet<string> terms)
    {
        if (terms.Any(term => result.FilePath.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return "semantic match + path keyword";
        }

        if (!string.IsNullOrWhiteSpace(result.Symbol) &&
            terms.Any(term => result.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return "semantic match + symbol keyword";
        }

        return "semantic match";
    }

    private static string CleanQuery(string query)
    {
        return string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static HashSet<string> Tokenize(string text)
    {
        return text
            .ToLowerInvariant()
            .Split(new[] { ' ', '/', '\\', '.', '-', '_', ',', ':', ';', '(', ')', '\n', '\t', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
