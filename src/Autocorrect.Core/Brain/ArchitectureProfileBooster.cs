namespace Autocorrect.Core.Brain;

// Uses pre-indexed architecture hubs to strengthen vague prompts without an LLM.
public static class ArchitectureProfileBooster
{
    public static void Apply(IList<RetrievalResult> results, string query, ProjectBrainData brain)
    {
        if (results.Count == 0 || brain.Architecture.Hubs.Count == 0)
        {
            return;
        }

        var parsed = PromptSymbolParser.Parse(query);
        if (parsed.Symbols.Count >= 2)
        {
            return;
        }

        var dominantLayers = InferLayers(query);
        var hubs = brain.Architecture.Hubs
            .Where(hub => dominantLayers.Count == 0 || dominantLayers.Contains(hub.Layer))
            .Take(6)
            .ToList();

        foreach (var hub in hubs)
        {
            var existing = results.FirstOrDefault(result =>
                result.FilePath.Equals(hub.FilePath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                existing.Score += 0.08;
                continue;
            }

            results.Add(new RetrievalResult
            {
                FilePath = hub.FilePath,
                Symbol = hub.Label,
                Score = 0.62,
                Reason = $"architecture hub ({hub.Layer})",
                ChunkType = hub.Layer,
                ContentPreview = hub.Label
            });
        }

        if (parsed.Symbols.Count > 0)
        {
            return;
        }

        foreach (var entry in brain.Architecture.EntryPoints.Take(4))
        {
            if (results.Any(result => result.FilePath.Equals(entry.FilePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            results.Add(new RetrievalResult
            {
                FilePath = entry.FilePath,
                Symbol = entry.Label,
                Score = entry.Score * 0.7,
                Reason = $"entry point ({entry.Kind})",
                ChunkType = "entry",
                ContentPreview = entry.Label
            });
        }

        var communityLayers = InferLayers(query);
        if (communityLayers.Count > 0)
        {
            foreach (var community in brain.Architecture.Communities.Where(item => communityLayers.Contains(item.Layer)).Take(2))
            {
                foreach (var file in community.FilePaths.Take(3))
                {
                    if (results.Any(result => result.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    results.Add(new RetrievalResult
                    {
                        FilePath = file,
                        Score = 0.58,
                        Reason = $"architecture community ({community.Name})",
                        ChunkType = community.Layer,
                        ContentPreview = community.Name
                    });
                }
            }
        }
    }

    private static HashSet<string> InferLayers(string query)
    {
        var lower = query.ToLowerInvariant();
        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ContainsAny(lower, "config", "setting", "appsettings", "option"))
        {
            layers.Add("config");
        }

        if (ContainsAny(lower, "ui", "view", "xaml", "screen", "page", "component"))
        {
            layers.Add("ui");
        }

        if (ContainsAny(lower, "api", "route", "endpoint", "controller"))
        {
            layers.Add("api");
        }

        if (ContainsAny(lower, "db", "database", "sql", "repository"))
        {
            layers.Add("data");
        }

        return layers;
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.Ordinal));
}
