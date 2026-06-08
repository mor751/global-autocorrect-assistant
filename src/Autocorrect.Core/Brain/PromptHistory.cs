using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Autocorrect.Core.Brain;

public sealed class PromptHistoryEntry
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public string ProjectRoot { get; set; } = string.Empty;
    public string OriginalPrompt { get; set; } = string.Empty;
    public string EnhancedPrompt { get; set; } = string.Empty;
    public List<string> RelevantFiles { get; set; } = new();
    public TaskType TaskType { get; set; }
    public bool? Accepted { get; set; }
    public bool? UserEdited { get; set; }
    public bool? Success { get; set; }
}

public sealed class UserPreferences
{
    public bool PrefersFullCode { get; set; }
    public bool WantsDetailedComments { get; set; }
    public bool PreserveRtl { get; set; }
    public bool PreserveExistingDesign { get; set; }
    public bool NoFunctionRemoval { get; set; }
    public bool WantsCursorReady { get; set; } = true;
    public List<string> FavoriteAnimationLibraries { get; set; } = new();

    public IEnumerable<string> AsInstructions()
    {
        if (PreserveExistingDesign) yield return "Preserve the existing design and layout; change only what is requested.";
        if (NoFunctionRemoval) yield return "Do not remove or rename existing functions/props unless explicitly asked.";
        if (PrefersFullCode) yield return "Return the full updated file(s), not partial snippets.";
        if (WantsDetailedComments) yield return "Add concise explanatory comments where intent is non-obvious.";
        if (PreserveRtl) yield return "Preserve Hebrew/RTL text and direction.";
        if (FavoriteAnimationLibraries.Count > 0) yield return $"Use {string.Join(", ", FavoriteAnimationLibraries)} for animation when relevant.";
    }
}

// Local, append-only prompt history that also derives durable user preferences. Never leaves the machine.
public sealed partial class PromptHistoryStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public PromptHistoryStore(string baseDirectory)
    {
        Directory.CreateDirectory(baseDirectory);
        _path = Path.Combine(baseDirectory, "prompt-history.jsonl");
    }

    public void Record(PromptHistoryEntry entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry, BrainJson.Options).Replace("\n", " ").Replace("\r", string.Empty);
            lock (_gate)
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch
        {
            // History is best-effort; failure must not block enhancement.
        }
    }

    public IReadOnlyList<PromptHistoryEntry> Recent(int limit = 200)
    {
        if (!File.Exists(_path))
        {
            return Array.Empty<PromptHistoryEntry>();
        }

        var entries = new List<PromptHistoryEntry>();
        try
        {
            foreach (var line in File.ReadLines(_path).Reverse().Take(limit))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<PromptHistoryEntry>(line, BrainJson.Options);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }
        catch
        {
            // Tolerate partial/corrupt lines.
        }

        return entries;
    }

    // Learns preferences from accumulated prompts using simple frequency thresholds.
    public UserPreferences LearnPreferences()
    {
        var prefs = new UserPreferences();
        var history = Recent();
        if (history.Count == 0)
        {
            return prefs;
        }

        var corpus = string.Join('\n', history.Select(h => h.OriginalPrompt + " " + h.EnhancedPrompt)).ToLowerInvariant();
        var total = history.Count;

        prefs.PrefersFullCode = CountSignals(history, "full code", "entire file", "whole file", "complete code") >= Math.Max(2, total / 5);
        prefs.WantsDetailedComments = CountSignals(history, "comment", "comments", "explain") >= Math.Max(2, total / 5);
        prefs.PreserveExistingDesign = CountSignals(history, "keep the design", "don't change the design", "same design", "keep the layout") >= 2;
        prefs.NoFunctionRemoval = CountSignals(history, "don't remove", "do not remove", "keep the functions", "keep all functions") >= 2;
        prefs.PreserveRtl = Hebrew().IsMatch(corpus);

        foreach (var (token, label) in new[] { ("gsap", "GSAP"), ("lenis", "Lenis"), ("framer", "Framer Motion"), ("anime", "anime.js") })
        {
            if (Regex.Matches(corpus, Regex.Escape(token)).Count >= 2)
            {
                prefs.FavoriteAnimationLibraries.Add(label);
            }
        }

        return prefs;
    }

    private static int CountSignals(IReadOnlyList<PromptHistoryEntry> history, params string[] phrases)
    {
        return history.Count(h => phrases.Any(p => h.OriginalPrompt.Contains(p, StringComparison.OrdinalIgnoreCase)));
    }

    [GeneratedRegex(@"[\u0590-\u05FF]")]
    private static partial Regex Hebrew();
}
