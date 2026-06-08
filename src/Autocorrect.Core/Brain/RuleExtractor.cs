using System.IO;
using System.Text.RegularExpressions;

namespace Autocorrect.Core.Brain;

// Derives project conventions from the stack, rule files, and content signals (RTL, animation libs).
public static partial class RuleExtractor
{
    public static List<string> Extract(string projectRoot, ProjectBrainData brain)
    {
        var rules = new List<string>();

        if (brain.Stack.Language == "TypeScript")
        {
            rules.Add("Use TypeScript with accurate, explicit types.");
        }

        if (!string.IsNullOrWhiteSpace(brain.Stack.Styling))
        {
            rules.Add($"Style with {brain.Stack.Styling}; match existing class/style patterns.");
        }

        if (brain.Stack.Framework == "Next.js")
        {
            rules.Add("Follow Next.js routing and file conventions (app/ or pages/).");
        }

        if (!string.IsNullOrWhiteSpace(brain.Stack.UiLibrary))
        {
            rules.Add($"Reuse {brain.Stack.UiLibrary} components instead of hand-rolling primitives.");
        }

        var animation = StackDetector.KnownAnimationLibraries(projectRoot);
        if (animation.Count > 0)
        {
            rules.Add($"Animations use {string.Join(", ", animation)}; prefer them over ad-hoc CSS animation.");
        }

        if (HasHebrewOrRtl(brain))
        {
            rules.Add("Preserve Hebrew/RTL text and direction (dir=\"rtl\").");
        }

        rules.AddRange(ReadRuleFiles(projectRoot));
        return rules.Distinct().Take(12).ToList();
    }

    private static bool HasHebrewOrRtl(ProjectBrainData brain)
    {
        return brain.Files.Any(f =>
            f.PreviewChunks.Any(chunk => Hebrew().IsMatch(chunk) || chunk.Contains("dir=\"rtl\"", StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<string> ReadRuleFiles(string projectRoot)
    {
        foreach (var candidate in new[] { ".cursorrules", "AGENTS.md", "CLAUDE.md" })
        {
            var path = Path.Combine(projectRoot, candidate);
            if (!File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                continue;
            }

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .Where(l => l.StartsWith("- ", StringComparison.Ordinal) || l.StartsWith("* ", StringComparison.Ordinal))
                         .Take(6))
            {
                yield return SecretScanner.Redact(line.TrimStart('-', '*', ' '));
            }
        }
    }

    [GeneratedRegex(@"[\u0590-\u05FF]")]
    private static partial Regex Hebrew();
}
