namespace Autocorrect.Cli.Commands;

internal sealed class PromptCommand : ICliCommand
{
    public string Name => "prompt";
    public string Summary => "Optimize a prompt with project context";

    public Task<int> RunAsync(string[] args, CliContext context) =>
        PromptCommandRunner.RunAsync(args, context, "Woody prompt");
}
