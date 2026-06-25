namespace Autocorrect.Core.Brain;

public sealed class RagRetrievalService
{
    private readonly IEmbeddingService _embeddings;
    private readonly IProjectVectorStore _vectorStore;
    private readonly FileVectorStore _fallbackStore;
    private readonly ProjectIndexMetadata? _metadata;
    private readonly AstRetrievalComposer _astComposer = new();

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

    public Task<RetrievalResponse> RetrieveAsync(
        string query,
        ProjectBrainData? brain,
        string projectRoot,
        int topK,
        CancellationToken cancellationToken) =>
        RetrieveAsync(query, brain, projectRoot, topK, RetrievalEnginePreference.Hybrid, cancellationToken);

    public async Task<RetrievalResponse> RetrieveAsync(
        string query,
        ProjectBrainData? brain,
        string projectRoot,
        int topK,
        RetrievalEnginePreference engine,
        CancellationToken cancellationToken)
    {
        if (brain is null || string.IsNullOrWhiteSpace(query))
        {
            return new RetrievalResponse { Query = query, RetrievalMode = RetrievalMode.NoBrain };
        }

        var cleaned = CleanQuery(query);
        var collection = _metadata?.Collection ?? BrainStorage.CollectionName(projectRoot);
        var vectorsReady = _metadata?.Status is ProjectBrainStatus.Ready or ProjectBrainStatus.PartialReady;

        if (engine == RetrievalEnginePreference.Rag && vectorsReady)
        {
            var ragOnly = await TryRetrieveRagOnlyAsync(cleaned, collection, topK, cancellationToken);
            if (ragOnly is not null)
            {
                return ragOnly;
            }
        }

        if (engine == RetrievalEnginePreference.Ast)
        {
            var astOnly = await TryRetrieveAstOnlyAsync(cleaned, brain, collection, topK, vectorsReady, cancellationToken);
            if (astOnly is not null)
            {
                return astOnly;
            }
        }

        if (engine == RetrievalEnginePreference.Hybrid)
        {
            var hybrid = await TryRetrieveHybridAsync(cleaned, brain, collection, topK, vectorsReady, cancellationToken);
            if (hybrid is not null)
            {
                return hybrid;
            }
        }

        if (engine != RetrievalEnginePreference.Hybrid)
        {
            return new RetrievalResponse
            {
                Query = cleaned,
                RetrievalMode = engine == RetrievalEnginePreference.Rag ? RetrievalMode.SemanticVector : RetrievalMode.AstGraph,
                Results = new List<RetrievalResult>()
            };
        }

        return await BuildKeywordFallbackAsync(cleaned, topK, cancellationToken);
    }

    private async Task<RetrievalResponse?> TryRetrieveRagOnlyAsync(
        string cleaned,
        string collection,
        int topK,
        CancellationToken cancellationToken)
    {
        var queryVector = await _embeddings.EmbedTextAsync($"query: {cleaned}", cancellationToken);
        if (queryVector is not { Length: > 0 })
        {
            return null;
        }

        var semantic = await _vectorStore.SearchAsync(collection, queryVector, Math.Max(topK * 2, topK), cancellationToken);
        var boosted = BoostAndDedupe(cleaned, semantic, topK);
        if (boosted.Count == 0)
        {
            return null;
        }

        return new RetrievalResponse
        {
            Query = cleaned,
            RetrievalMode = RetrievalMode.SemanticVector,
            Results = boosted
        };
    }

    private async Task<RetrievalResponse?> TryRetrieveAstOnlyAsync(
        string cleaned,
        ProjectBrainData brain,
        string collection,
        int topK,
        bool vectorsReady,
        CancellationToken cancellationToken)
    {
        var results = await _astComposer.CollectAsync(cleaned, brain, collection, topK, vectorsReady, _vectorStore, cancellationToken);
        var ranked = BoostAndDedupe(cleaned, results, topK * 2);
        ranked = AstRetrievalComposer.RankLineFirst(ranked, topK);
        if (ranked.Count == 0)
        {
            return null;
        }

        return new RetrievalResponse
        {
            Query = cleaned,
            RetrievalMode = RetrievalMode.AstGraph,
            Results = ranked
        };
    }

    private async Task<RetrievalResponse?> TryRetrieveHybridAsync(
        string cleaned,
        ProjectBrainData brain,
        string collection,
        int topK,
        bool vectorsReady,
        CancellationToken cancellationToken)
    {
        var astResults = await _astComposer.CollectAsync(cleaned, brain, collection, topK, vectorsReady, _vectorStore, cancellationToken);
        var merged = astResults.ToList();
        if (vectorsReady)
        {
            var queryVector = await _embeddings.EmbedTextAsync($"query: {cleaned}", cancellationToken);
            if (queryVector is { Length: > 0 })
            {
                var semantic = await _vectorStore.SearchAsync(collection, queryVector, Math.Max(topK * 2, topK), cancellationToken);
                merged.AddRange(semantic);
            }
        }

        if (merged.Count == 0)
        {
            return null;
        }

        var ranked = BoostAndDedupe(cleaned, merged, topK * 3);
        ranked = AstRetrievalComposer.RankLineFirst(ranked, topK);
        if (ranked.Count == 0)
        {
            return null;
        }

        return new RetrievalResponse
        {
            Query = cleaned,
            RetrievalMode = vectorsReady ? RetrievalMode.HybridVectorKeyword : RetrievalMode.AstGraph,
            Results = ranked
        };
    }

    private async Task<RetrievalResponse> BuildKeywordFallbackAsync(string cleaned, int topK, CancellationToken cancellationToken)
    {
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
