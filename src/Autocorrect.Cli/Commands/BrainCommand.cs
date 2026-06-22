using Autocorrect.Cli.Brain;

namespace Autocorrect.Cli.Commands;

internal sealed class BrainCommand : ICliCommand
{
    public string Name => "brain";
    public string Summary => "Open the vector + AST brain UI on localhost";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        string projectRoot;
        try
        {
            projectRoot = CliArgs.RequireProject(args, context);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintIndexedProjects(context);
            return 1;
        }

        if (!context.Brain.IsIndexed(projectRoot))
        {
            Console.Error.WriteLine($"Project is not indexed: {projectRoot}");
            PrintIndexedProjects(context);
            Console.Error.WriteLine($"Run: woody index \"{projectRoot}\"");
            return 1;
        }

        var sub = CliArgs.Positional(args, 0);
        if (!string.IsNullOrWhiteSpace(sub) && !sub.Equals("open", StringComparison.OrdinalIgnoreCase) && !sub.StartsWith('-'))
        {
            Console.Error.WriteLine("Usage: woody brain [open] [--port 7842] [--no-browser] [--path <folder>]");
            return 1;
        }

        var port = CliArgs.IntFlag(args, "--port", 7842);
        var openBrowser = !args.Any(arg => arg.Equals("--no-browser", StringComparison.OrdinalIgnoreCase));
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var resolution = await BrainPortResolver.ResolveAsync(port, openBrowser, cts.Token);
        if (resolution.AlreadyRunning)
        {
            return 0;
        }

        var host = new BrainHost(context, projectRoot, resolution.Port, openBrowser);
        return await host.RunAsync(cts.Token);
    }

    private static void PrintIndexedProjects(CliContext context)
    {
        var indexed = context.Brain.ListIndexedProjects();
        if (indexed.Count == 0)
        {
            return;
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("Indexed projects:");
        foreach (var path in indexed)
        {
            Console.Error.WriteLine($"  {path}");
        }
    }
}
