using Autocorrect.Core.Brain;

namespace Autocorrect.Cli;

internal static class PromptCommandRunner
{
    public static async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.ResolveProject(args, context);
        var prompt = CliArgs.RequirePromptText(args);
        var engine = CliArgs.ParseRetrievalEngine(args);
        var outcome = await context.Brain.EnhanceAsync(prompt, projectRoot, CancellationToken.None, retrievalEngine: engine);
        CliConsolePresenter.WritePromptOutcome(outcome, projectRoot, prompt, engine);
        return outcome.Status is EnhancementStatus.MissingContext ? 2 : 0;
    }
}
