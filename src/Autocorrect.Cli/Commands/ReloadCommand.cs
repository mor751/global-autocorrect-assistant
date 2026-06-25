using Autocorrect.Core.Brain;

namespace Autocorrect.Cli.Commands;

internal sealed class ReloadCommand : ICliCommand
{
    public string Name => "reload";
    public string Summary => "Rescan folder files and update brain when something changed";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.RequireProject(args, context);
        var force = args.Any(arg => arg is "--force" or "-f");
        Console.WriteLine(force
            ? $"Force reloading {projectRoot} ..."
            : $"Checking {projectRoot} for file changes ...");

        var report = await context.Brain.SyncAsync(projectRoot, force, CancellationToken.None);
        Console.WriteLine(report.Message);
        if (report.SyncPerformed)
        {
            Console.WriteLine(
                $"Files: {report.TotalFiles:N0}  Chunks: {report.TotalChunks:N0}  Vectors: {report.EmbeddedChunks:N0}");
            if (report.AddedFiles + report.ModifiedFiles + report.RemovedFiles > 0)
            {
                Console.WriteLine(
                    $"Changes: +{report.AddedFiles} added, ~{report.ModifiedFiles} modified, -{report.RemovedFiles} removed");
            }
        }

        return 0;
    }
}
