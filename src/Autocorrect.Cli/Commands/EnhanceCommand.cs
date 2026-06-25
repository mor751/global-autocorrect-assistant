namespace Autocorrect.Cli.Commands;

internal sealed class EnhanceCommand : ICliCommand
{
    public string Name => "enhance";

    public string Summary => "Hidden alias for prompt";

    public Task<int> RunAsync(string[] args, CliContext context) =>
        PromptCommandRunner.RunAsync(args, context);
}
