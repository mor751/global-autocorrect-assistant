using Autocorrect.Core.Brain;

namespace Autocorrect.Cli.Commands;

internal sealed class IndexCommand : ICliCommand
{
    public string Name => "index";
    public string Summary => "Build the project brain (scan, AST, vectors)";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.RequireProject(args, context);
        WoodyConsole.WriteBrandBanner();
        WoodyConsole.WriteCommandHeader("index", "Scanning, parsing AST, and embedding the project brain.");
        WoodyConsole.WriteMeta("Project", projectRoot);
        Console.WriteLine();

        var brain = await context.Brain.IndexAsync(projectRoot, CancellationToken.None);
        var metadata = context.Brain.LoadIndexMetadata(projectRoot);
        WoodyConsole.WriteSuccess($"Indexed {brain.Files.Count:N0} files");
        WoodyConsole.WriteMeta("Chunks", $"{metadata?.TotalChunks ?? 0:N0}");
        WoodyConsole.WriteMeta("Vectors", $"{metadata?.EmbeddedChunks ?? 0:N0}");
        WoodyConsole.WriteMeta("Status", metadata?.Status.ToString() ?? "unknown");
        WoodyConsole.WriteMeta("AST nodes", $"{brain.Graph.Nodes.Count:N0}");
        WoodyConsole.WriteMeta("Communities", $"{brain.Architecture.Communities.Count:N0}");
        Console.WriteLine();
        WoodyConsole.WriteDim("Next: woody brain  or  woody prompt \"your task\"");
        Console.WriteLine();
        return 0;
    }
}
