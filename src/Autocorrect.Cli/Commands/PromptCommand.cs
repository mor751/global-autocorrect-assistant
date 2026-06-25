namespace Autocorrect.Cli.Commands;

internal sealed class PromptCommand : ICliCommand
{
    public string Name => "prompt";
    public string Summary => "Compile a short agent prompt with file:line targets";

    public Task<int> RunAsync(string[] args, CliContext context) =>
        PromptCommandRunner.RunAsync(args, context);
}
