namespace Autocorrect.Core.Brain;

public enum TaskType
{
    Ui,
    Bugfix,
    Refactor,
    Feature,
    Debug,
    Docs,
    Test,
    Unknown
}

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public sealed class RetrievedFile
{
    public ProjectFileSummary File { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public double Score { get; set; }
}

public sealed class PromptAnalysis
{
    public double QualityScore { get; set; }
    public double ClarityScore { get; set; }
    public double ContextScore { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public string DetectedIntent { get; set; } = string.Empty;
    public TaskType TaskType { get; set; }
    public List<string> MissingContext { get; set; } = new();
    public List<string> LikelyRelevantFiles { get; set; } = new();
    public bool ShouldAskUserForMoreInfo { get; set; }
    public string Reason { get; set; } = string.Empty;
}

// Scores prompt quality and intent using heuristics over the prompt text, project brain, and retrieval.
public sealed class PromptAnalyzer
{
    private static readonly string[] VaguePhrases =
    {
        "make it better", "make this better", "fix this", "fix it", "change the design",
        "improve this", "do something", "make it nice", "clean it up", "make it work"
    };

    public PromptAnalysis Analyze(
        string prompt,
        ProjectBrainData? brain,
        IReadOnlyList<RetrievedFile> retrieved,
        UserPreferences preferences)
    {
        var text = prompt.Trim();
        var lower = text.ToLowerInvariant();
        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        var analysis = new PromptAnalysis
        {
            TaskType = DetectTaskType(lower),
            LikelyRelevantFiles = retrieved.Take(6).Select(r => r.File.Path).ToList()
        };

        analysis.DetectedIntent = DescribeIntent(analysis.TaskType, lower);
        analysis.ClarityScore = ScoreClarity(lower, wordCount);
        analysis.ContextScore = ScoreContext(brain, retrieved, lower);
        analysis.QualityScore = Math.Round(analysis.ClarityScore * 0.55 + analysis.ContextScore * 0.45, 2);
        analysis.MissingContext = DetectMissingContext(analysis.TaskType, lower, retrieved, brain, preferences);
        analysis.ShouldAskUserForMoreInfo = analysis.ClarityScore < 0.45 || (analysis.QualityScore < 0.5 && analysis.MissingContext.Count > 0);
        analysis.RiskLevel = ScoreRisk(analysis, retrieved);
        analysis.Reason = BuildReason(analysis, wordCount);
        return analysis;
    }

    private static TaskType DetectTaskType(string lower)
    {
        if (ContainsAny(lower, "animation", "animate", "transition", "scroll", "parallax", "design", "ui", "layout", "style", "css", "color", "responsive", "page look"))
        {
            return TaskType.Ui;
        }

        if (ContainsAny(lower, "bug", "error", "crash", "not working", "broken", "fails", "exception", "undefined", "null reference"))
        {
            return TaskType.Bugfix;
        }

        if (ContainsAny(lower, "refactor", "clean up", "restructure", "rename", "extract", "simplify"))
        {
            return TaskType.Refactor;
        }

        if (ContainsAny(lower, "debug", "why is", "investigate", "trace", "log", "reproduce"))
        {
            return TaskType.Debug;
        }

        if (ContainsAny(lower, "test", "unit test", "coverage", "spec"))
        {
            return TaskType.Test;
        }

        if (ContainsAny(lower, "doc", "readme", "comment", "explain in docs"))
        {
            return TaskType.Docs;
        }

        if (ContainsAny(lower, "add", "create", "build", "implement", "new feature", "support"))
        {
            return TaskType.Feature;
        }

        return TaskType.Unknown;
    }

    private static string DescribeIntent(TaskType type, string lower)
    {
        return type switch
        {
            TaskType.Ui => "Adjust the user interface or visual design",
            TaskType.Bugfix => "Fix a defect in existing behavior",
            TaskType.Refactor => "Restructure code without changing behavior",
            TaskType.Debug => "Investigate unexpected behavior",
            TaskType.Feature => "Add new functionality",
            TaskType.Test => "Add or improve tests",
            TaskType.Docs => "Write or update documentation",
            _ => lower.Length > 0 ? "General code assistance" : "Unclear"
        };
    }

    private static double ScoreClarity(string lower, int wordCount)
    {
        double score = 0.5;
        if (VaguePhrases.Any(p => lower.Contains(p, StringComparison.Ordinal)))
        {
            score -= 0.3;
        }

        if (wordCount >= 6)
        {
            score += 0.15;
        }

        if (wordCount >= 14)
        {
            score += 0.1;
        }

        if (wordCount <= 3)
        {
            score -= 0.2;
        }

        if (ContainsAny(lower, ".tsx", ".ts", ".js", ".jsx", ".cs", ".py", ".css", "component", "function", "file", "route", "endpoint"))
        {
            score += 0.2;
        }

        if (lower.Contains("keep", StringComparison.Ordinal) || lower.Contains("don't", StringComparison.Ordinal) || lower.Contains("only", StringComparison.Ordinal))
        {
            score += 0.05;
        }

        return Math.Clamp(score, 0, 1);
    }

    private static double ScoreContext(ProjectBrainData? brain, IReadOnlyList<RetrievedFile> retrieved, string lower)
    {
        if (brain is null)
        {
            return 0.2;
        }

        double score = 0.35;
        if (retrieved.Count > 0)
        {
            score += Math.Min(0.4, retrieved.Sum(r => r.Score) * 0.15 + retrieved.Count * 0.05);
        }

        if (retrieved.Any(r => lower.Contains(System.IO.Path.GetFileNameWithoutExtension(r.File.Path).ToLowerInvariant(), StringComparison.Ordinal)))
        {
            score += 0.15;
        }

        return Math.Clamp(score, 0, 1);
    }

    private static List<string> DetectMissingContext(
        TaskType type,
        string lower,
        IReadOnlyList<RetrievedFile> retrieved,
        ProjectBrainData? brain,
        UserPreferences preferences)
    {
        var missing = new List<string>();

        if (brain is null)
        {
            missing.Add("No project folder is indexed yet — select one for project-aware results.");
        }

        if (retrieved.Count == 0 && brain is not null)
        {
            missing.Add("Which file/component/page should this target? No clearly relevant file was found.");
        }

        switch (type)
        {
            case TaskType.Ui when lower.Contains("image", StringComparison.Ordinal) || lower.Contains("screenshot", StringComparison.Ordinal) || lower.Contains("match the", StringComparison.Ordinal):
                missing.Add("Attach the reference image/screenshot; it is required to match a design.");
                break;
            case TaskType.Ui:
                if (!retrieved.Any(r => r.File.Role is FileRole.Component or FileRole.Route or FileRole.Style))
                {
                    missing.Add("Name the component/page/style to change.");
                }

                break;
            case TaskType.Bugfix:
            case TaskType.Debug:
                if (!lower.Contains("error", StringComparison.Ordinal) && !lower.Contains("expected", StringComparison.Ordinal))
                {
                    missing.Add("Paste the exact error message and what you expected vs. what happened.");
                }

                break;
            case TaskType.Feature:
                if (lower.Split(' ').Length < 8)
                {
                    missing.Add("Describe the expected behavior/acceptance criteria for the new feature.");
                }

                break;
        }

        if (preferences.PrefersFullCode && type is TaskType.Feature or TaskType.Refactor or TaskType.Bugfix)
        {
            // Preference is satisfied later by the rewriter's instructions; nothing missing here.
        }

        return missing.Distinct().ToList();
    }

    private static RiskLevel ScoreRisk(PromptAnalysis analysis, IReadOnlyList<RetrievedFile> retrieved)
    {
        if (analysis.ClarityScore < 0.4 || (analysis.ContextScore < 0.4 && retrieved.Count == 0))
        {
            return RiskLevel.High;
        }

        return analysis.QualityScore >= 0.7 ? RiskLevel.Low : RiskLevel.Medium;
    }

    private static string BuildReason(PromptAnalysis analysis, int wordCount)
    {
        if (analysis.ShouldAskUserForMoreInfo)
        {
            return "Prompt is vague or under-specified; clarifying it now avoids expensive AI retries.";
        }

        return analysis.ContextScore >= 0.6
            ? "Prompt is clear and matches concrete project files."
            : "Prompt is workable; adding the relevant file paths will sharpen it.";
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));
}
