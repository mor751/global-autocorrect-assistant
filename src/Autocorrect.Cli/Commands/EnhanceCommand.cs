namespace Autocorrect.Cli.Commands;

internal sealed class EnhanceCommand : ICliCommand
{
    public string Name => "enhance";
    public string Summary => "Alias for prompt — optimize a prompt with project context";

    public Task<int> RunAsync(string[] args, CliContext context) =>
        PromptCommandRunner.RunAsync(args, context, "Woody enhance");
}
