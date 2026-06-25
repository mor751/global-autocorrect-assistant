using Autocorrect.Core.Brain;

namespace Autocorrect.Cli.Commands;

internal sealed class ReloadCommand : ICliCommand
{
    public string Name => "reload";
    public string Summary => "Sync and reload when project files changed";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.RequireProject(args, context);
        var force = args.Any(arg => arg is "--force" or "-f");

        WoodyConsole.WriteBrandBanner();
        WoodyConsole.WriteCommandHeader(
            force ? "reload --force" : "reload",
            force ? "Full rescan and re-index of the project brain." : "Sync brain with changed project files.");
        WoodyConsole.WriteMeta("Project", projectRoot);
        Console.WriteLine();

        var report = await context.Brain.SyncAsync(projectRoot, force, CancellationToken.None);
        if (report.SyncPerformed)
        {
            WoodyConsole.WriteSuccess(report.Message);
            WoodyConsole.WriteMeta("Files", $"{report.TotalFiles:N0}");
            WoodyConsole.WriteMeta("Chunks", $"{report.TotalChunks:N0}");
            WoodyConsole.WriteMeta("Vectors", $"{report.EmbeddedChunks:N0}");
            if (report.AddedFiles + report.ModifiedFiles + report.RemovedFiles > 0)
            {
                WoodyConsole.WriteMeta("Changes", $"+{report.AddedFiles} · ~{report.ModifiedFiles} · -{report.RemovedFiles}");
            }
        }
        else
        {
            WoodyConsole.WriteDim(report.Message);
        }

        Console.WriteLine();
        return 0;
    }
}
