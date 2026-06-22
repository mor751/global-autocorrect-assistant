using Autocorrect.Core.Brain;

namespace Autocorrect.Cli.Commands;

internal sealed class IndexCommand : ICliCommand
{
    public string Name => "index";
    public string Summary => "Scan, chunk, embed, and build the project brain";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.RequireProject(args, context);
        Console.WriteLine($"Indexing {projectRoot} ...");
        var brain = await context.Brain.IndexAsync(projectRoot, CancellationToken.None);
        var metadata = context.Brain.LoadIndexMetadata(projectRoot);
        Console.WriteLine($"Done: {brain.Files.Count} files, {metadata?.TotalChunks ?? 0} chunks, {metadata?.EmbeddedChunks ?? 0} vectors, status {metadata?.Status}");
        return 0;
    }
}
