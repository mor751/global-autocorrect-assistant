using Autocorrect.Core.Brain;

namespace Autocorrect.Cli;

internal static class CliConsolePresenter
{
    public static void WritePromptOutcome(
        EnhancementOutcome outcome,
        string projectRoot,
        string originalPrompt,
        RetrievalEnginePreference engine = RetrievalEnginePreference.Hybrid)
    {
        WoodyConsole.WriteBrandBanner();
        WoodyConsole.WriteCommandHeader("prompt", "Copy the optimized prompt into Cursor, Claude Code, or Codex.");
        WoodyConsole.WriteMeta("Engine", CliArgs.DescribeEngine(engine));
        WoodyConsole.WriteMeta("Project", Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        WoodyConsole.WriteMeta("Status", HumanStatus(outcome.Status));
        WoodyConsole.WriteMeta("Brain", outcome.ProjectIndexed ? $"{outcome.RetrievalMode} · {outcome.VectorCount:N0} vectors" : "not indexed");
        WoodyConsole.WriteMeta("Writer", outcome.OllamaAvailable ? "gemma3:4b" : "deterministic fallback");
        WoodyConsole.WriteMeta("Tokens saved", $"~{outcome.DownstreamTokenSavingsEstimate:N0} downstream (est.)");

        WoodyConsole.WriteDivider("ORIGINAL");
        WoodyConsole.WriteBlock(originalPrompt);

        WoodyConsole.WriteDivider("OPTIMIZED");
        WoodyConsole.WriteBlock(outcome.Result.ImprovedPrompt, ConsoleColor.White);

        if (outcome.Result.MissingContext.Count > 0)
        {
            WoodyConsole.WriteDivider("NOTES");
            foreach (var item in outcome.Result.MissingContext.Take(8))
            {
                WoodyConsole.WriteWarn(item);
            }
        }

        Console.WriteLine();
        WoodyConsole.WriteSuccess("Copy the OPTIMIZED section into your agent.");
        Console.WriteLine();
    }

    private static string HumanStatus(EnhancementStatus status) => status switch
    {
        EnhancementStatus.ImprovedReady => "Ready",
        EnhancementStatus.MissingContext => "Needs more context",
        EnhancementStatus.NotIndexed => "Generic (project not indexed)",
        EnhancementStatus.OllamaFallback => "Fallback (Ollama offline)",
        _ => status.ToString()
    };
}
