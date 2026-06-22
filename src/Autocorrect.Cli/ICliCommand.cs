namespace Autocorrect.Cli;

internal interface ICliCommand
{
    string Name { get; }
    string Summary { get; }
    Task<int> RunAsync(string[] args, CliContext context);
}
