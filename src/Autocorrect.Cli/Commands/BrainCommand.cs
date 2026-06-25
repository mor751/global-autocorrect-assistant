using Autocorrect.Cli.Brain;

namespace Autocorrect.Cli.Commands;

internal sealed class BrainCommand : ICliCommand
{
    public string Name => "brain";
    public string Summary => "Open AST + vector brain in the browser";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        string projectRoot;
        try
        {
            projectRoot = CliArgs.RequireProject(args, context);
        }
        catch (Exception ex)
        {
            WoodyConsole.WriteError(ex.Message);
            PrintIndexedProjects(context);
            return 1;
        }

        if (!context.Brain.IsIndexed(projectRoot))
        {
            WoodyConsole.WriteError($"Project is not indexed: {projectRoot}");
            PrintIndexedProjects(context);
            WoodyConsole.WriteDim($"Run: woody index --path \"{projectRoot}\"");
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

        WoodyConsole.WriteBrandBanner();
        WoodyConsole.WriteCommandHeader("brain", "AST graph + vector brain in your browser.");
        WoodyConsole.WriteMeta("Project", Path.GetFileName(projectRoot));
        Console.WriteLine();

        var resolution = await BrainPortResolver.ResolveAsync(port, openBrowser, cts.Token);
        if (resolution.AlreadyRunning)
        {
            WoodyConsole.WriteSuccess($"Brain already running at {resolution.Url}");
            return 0;
        }

        var host = new BrainHost(context, projectRoot, resolution.Port, openBrowser);
        WoodyConsole.WriteSuccess($"Opening {resolution.Url}");
        WoodyConsole.WriteDim("Press Ctrl+C to stop the brain server.");
        Console.WriteLine();
        return await host.RunAsync(cts.Token);
    }

    private static void PrintIndexedProjects(CliContext context)
    {
        var indexed = context.Brain.ListIndexedProjects();
        if (indexed.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        WoodyConsole.WriteDivider("INDEXED PROJECTS");
        foreach (var path in indexed)
        {
            WoodyConsole.WriteDim(path);
        }
    }
}
