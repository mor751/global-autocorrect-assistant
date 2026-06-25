using Autocorrect.Core.Brain;

namespace Autocorrect.Cli;

internal static class CliConsolePresenter
{
    private const int Width = 72;

    public static void WritePromptOutcome(
        EnhancementOutcome outcome,
        string projectRoot,
        string originalPrompt,
        RetrievalEnginePreference engine = RetrievalEnginePreference.Hybrid)
    {
        WriteBanner("WOODY PROMPT");
        WriteMetaRow("Engine", CliArgs.DescribeEngine(engine));
        WriteMetaRow("Retrieval", outcome.RetrievalMode.ToString());
        WriteMetaRow("Project", Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        WriteMetaRow("Path", projectRoot);
        WriteMetaRow("Status", HumanStatus(outcome.Status));
        WriteMetaRow("Brain", outcome.ProjectIndexed ? $"{outcome.RetrievalMode} | {outcome.VectorCount:N0} vectors" : "not indexed");
        WriteMetaRow("Writer", outcome.OllamaAvailable ? "gemma3:4b" : "deterministic fallback");
        WriteMetaRow("Accuracy", $"{Percent(outcome.Result.Confidence)} confidence");
        WriteMetaRow("Tokens saved", $"~{outcome.DownstreamTokenSavingsEstimate:N0} downstream (est.)");
        WriteMetaRow("Use with", $"{AgentModelAdvisor.AgentLabel(outcome.TargetAgent)} -> {outcome.RecommendedModels}");

        WriteDivider("ORIGINAL PROMPT");
        WriteBlock(originalPrompt);

        WriteDivider("OPTIMIZED PROMPT");
        WriteBlock(outcome.Result.ImprovedPrompt);

        if (outcome.Result.MissingContext.Count > 0)
        {
            WriteDivider("MISSING CONTEXT");
            foreach (var item in outcome.Result.MissingContext.Take(8))
            {
                Console.WriteLine($"  ! {item}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', Width));
        Console.WriteLine("Copy the OPTIMIZED PROMPT section into your agent.");
        Console.WriteLine();
    }

    private static void WriteBanner(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', Width));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('=', Width));
        Console.WriteLine();
    }

    private static void WriteMetaRow(string label, string value)
    {
        Console.WriteLine($"  {label,-14} {value}");
    }

    private static void WriteDivider(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', Width));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('-', Width));
        Console.WriteLine();
    }

    private static void WriteBlock(string text)
    {
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            Console.WriteLine($"  {line}");
        }
    }

    private static string HumanStatus(EnhancementStatus status) => status switch
    {
        EnhancementStatus.ImprovedReady => "Ready",
        EnhancementStatus.MissingContext => "Needs more context",
        EnhancementStatus.NotIndexed => "Generic (project not indexed)",
        EnhancementStatus.OllamaFallback => "Fallback (Ollama offline)",
        _ => status.ToString()
    };

    private static string Percent(double value) => $"{(int)Math.Round(Math.Clamp(value, 0, 1) * 100)}%";
}
