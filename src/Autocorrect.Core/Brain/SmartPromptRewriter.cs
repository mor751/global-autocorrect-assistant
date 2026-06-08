using System.Text;

namespace Autocorrect.Core.Brain;

public enum EnhancementKind
{
    ImprovedPrompt,
    ShorterPrompt,
    MissingContextWarning
}

public sealed class EnhancedPromptResult
{
    public EnhancementKind Kind { get; set; }
    public string ImprovedPrompt { get; set; } = string.Empty;
    public string ShorterPrompt { get; set; } = string.Empty;
    public string Task { get; set; } = string.Empty;
    public string OriginalIntent { get; set; } = string.Empty;
    public List<string> MissingContext { get; set; } = new();
    public List<string> RelevantFiles { get; set; } = new();
    public int EstimatedPromptTokenChange { get; set; }
    public double EstimatedReducedRetries { get; set; }
    public double Confidence { get; set; }
    public bool UsedOllama { get; set; }
}

// Turns a messy prompt into a structured, project-aware prompt; also offers a compressed variant and a vague-prompt warning.
public sealed class SmartPromptRewriter
{
    private readonly IOllamaClient? _ollama;

    public SmartPromptRewriter(IOllamaClient? ollama)
    {
        _ollama = ollama;
    }

    public async Task<EnhancedPromptResult> BuildAsync(
        string originalPrompt,
        PromptAnalysis analysis,
        ProjectBrainData? brain,
        IReadOnlyList<RetrievedFile> retrieved,
        UserPreferences preferences,
        bool ollamaAvailable,
        CancellationToken cancellationToken)
    {
        var clean = SecretScanner.Redact(originalPrompt.Trim());
        var task = await RefineTaskAsync(clean, ollamaAvailable, cancellationToken);
        var relevantLines = retrieved
            .Select(r => $"{r.File.Path} — {RelevanceReason(r)}")
            .Take(6)
            .ToList();

        var result = new EnhancedPromptResult
        {
            Task = task,
            OriginalIntent = Shorten(clean, 220),
            MissingContext = analysis.MissingContext,
            RelevantFiles = relevantLines,
            Confidence = Math.Round(0.4 + analysis.QualityScore * 0.55, 2),
            UsedOllama = ollamaAvailable && _ollama is not null
        };

        result.ImprovedPrompt = BuildStructuredPrompt(task, brain, relevantLines, analysis, preferences);
        result.ShorterPrompt = BuildShortPrompt(task, relevantLines, preferences);
        result.Kind = ChooseKind(analysis, clean);

        var originalTokens = EstimateTokens(clean);
        var improvedTokens = EstimateTokens(result.ImprovedPrompt);
        result.EstimatedPromptTokenChange = improvedTokens - originalTokens;
        result.EstimatedReducedRetries = EstimateReducedRetries(analysis);
        return result;
    }

    private async Task<string> RefineTaskAsync(string prompt, bool ollamaAvailable, CancellationToken cancellationToken)
    {
        if (ollamaAvailable && _ollama is not null)
        {
            var instruction = "Rewrite the following into a single clear, imperative task line for a coding agent. " +
                              "Keep all concrete details, no preamble, output only the line.\n\n" + prompt;
            var refined = await _ollama.GenerateAsync(instruction, cancellationToken);
            if (!string.IsNullOrWhiteSpace(refined))
            {
                return SecretScanner.Redact(FirstLine(refined!));
            }
        }

        return Capitalize(CollapseWhitespace(prompt));
    }

    private static string BuildStructuredPrompt(
        string task,
        ProjectBrainData? brain,
        IReadOnlyList<string> relevantLines,
        PromptAnalysis analysis,
        UserPreferences preferences)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Task:").AppendLine(task).AppendLine();

        builder.AppendLine("Project context:");
        if (brain is not null)
        {
            var stack = brain.Stack.Describe().ToList();
            if (stack.Count > 0)
            {
                builder.AppendLine($"- Stack: {string.Join(", ", stack)}");
            }

            foreach (var rule in brain.Rules.Take(5))
            {
                builder.AppendLine($"- {rule}");
            }
        }
        else
        {
            builder.AppendLine("- No project indexed; treat this as a general coding request.");
        }

        builder.AppendLine();
        builder.AppendLine("Relevant files:");
        if (relevantLines.Count > 0)
        {
            foreach (var line in relevantLines)
            {
                builder.AppendLine($"- {line}");
            }
        }
        else
        {
            builder.AppendLine("- None auto-detected; ask which files to start with before editing.");
        }

        builder.AppendLine();
        builder.AppendLine("Instructions:");
        builder.AppendLine("- Keep existing logic unless explicitly asked to change it.");
        builder.AppendLine("- Do not rename props/routes/functions unless necessary.");
        builder.AppendLine("- Do not scan unrelated folders.");
        builder.AppendLine("- Start with the listed relevant files.");
        builder.AppendLine("- Ask before modifying unrelated files.");
        builder.AppendLine("- Return full updated code only when code changes are requested.");
        builder.AppendLine("- Explain briefly only if needed.");
        foreach (var preference in preferences.AsInstructions())
        {
            builder.AppendLine($"- {preference}");
        }

        if (analysis.MissingContext.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Missing info:");
            foreach (var missing in analysis.MissingContext)
            {
                builder.AppendLine($"- {missing}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Original user intent:");
        builder.Append(analysis.DetectedIntent);
        return builder.ToString().TrimEnd();
    }

    private static string BuildShortPrompt(string task, IReadOnlyList<string> relevantLines, UserPreferences preferences)
    {
        var builder = new StringBuilder();
        builder.Append(task);
        if (relevantLines.Count > 0)
        {
            builder.Append(" Files: ").Append(string.Join("; ", relevantLines.Select(l => l.Split(" — ")[0])));
        }

        builder.Append(" Keep existing logic; edit only these files; ask before touching others.");
        if (preferences.PrefersFullCode)
        {
            builder.Append(" Return full updated code.");
        }

        return builder.ToString();
    }

    private static EnhancementKind ChooseKind(PromptAnalysis analysis, string original)
    {
        if (analysis.ShouldAskUserForMoreInfo && analysis.MissingContext.Count > 0)
        {
            return EnhancementKind.MissingContextWarning;
        }

        var verbose = original.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 45;
        return verbose && analysis.ContextScore >= 0.5 ? EnhancementKind.ShorterPrompt : EnhancementKind.ImprovedPrompt;
    }

    private static double EstimateReducedRetries(PromptAnalysis analysis)
    {
        return analysis.RiskLevel switch
        {
            RiskLevel.High => analysis.ShouldAskUserForMoreInfo ? 2.5 : 1.5,
            RiskLevel.Medium => 1.2,
            _ => 0.6
        };
    }

    private static string RelevanceReason(RetrievedFile retrieved)
    {
        var role = retrieved.File.Role switch
        {
            FileRole.Component => "UI component",
            FileRole.Route => "page/route",
            FileRole.Hook => "hook",
            FileRole.Api => "API handler",
            FileRole.Style => "styles",
            FileRole.Util => "utility",
            FileRole.Database => "data/schema",
            FileRole.Config => "config",
            _ => "related file"
        };

        return string.IsNullOrWhiteSpace(retrieved.Reason) ? role : $"{role}, {retrieved.Reason}";
    }

    private static int EstimateTokens(string text) => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Round(text.Length / 4.0));

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    private static string CollapseWhitespace(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string FirstLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? text.Trim();
}
