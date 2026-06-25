namespace Autocorrect.Core.Brain;

// Boosts graph hits that match architecture words in the prompt (config, ui, api).
public static class ArchitectureIntentBooster
{
    public static void Apply(IList<RetrievalResult> results, string query, ProjectBrainData brain)
    {
        if (results.Count == 0)
        {
            return;
        }

        var intents = InferIntents(query);
        if (intents.Count == 0)
        {
            return;
        }

        var roleByPath = brain.Files.ToDictionary(file => file.Path, file => file.Role, StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (!roleByPath.TryGetValue(result.FilePath, out var role))
            {
                continue;
            }

            foreach (var intent in intents)
            {
                if (!MatchesIntent(role, result, intent))
                {
                    continue;
                }

                result.Score += 0.14;
                result.Reason = string.IsNullOrWhiteSpace(result.Reason)
                    ? $"architecture match ({intent})"
                    : $"{result.Reason} + {intent}";
            }
        }
    }

    private static HashSet<string> InferIntents(string query)
    {
        var lower = query.ToLowerInvariant();
        var intents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ContainsAny(lower, "config", "configure", "configured", "setting", "settings", "option", "appsettings"))
        {
            intents.Add("config");
        }

        if (ContainsAny(lower, "ui", "view", "xaml", "component", "screen", "page", "window", "frontend"))
        {
            intents.Add("ui");
        }

        if (ContainsAny(lower, "api", "route", "endpoint", "controller", "handler"))
        {
            intents.Add("api");
        }

        if (ContainsAny(lower, "database", "db", "sql", "repository", "migration"))
        {
            intents.Add("data");
        }

        return intents;
    }

    private static bool MatchesIntent(FileRole role, RetrievalResult result, string intent) =>
        intent switch
        {
            "config" => role is FileRole.Config || result.ChunkType.Contains("config", StringComparison.OrdinalIgnoreCase),
            "ui" => role is FileRole.Component or FileRole.Route or FileRole.Style,
            "api" => role is FileRole.Api or FileRole.Route,
            "data" => role is FileRole.Database,
            _ => false
        };

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.Ordinal));
}
