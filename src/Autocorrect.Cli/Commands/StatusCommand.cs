namespace Autocorrect.Cli.Commands;

internal sealed class StatusCommand : ICliCommand
{
    public string Name => "status";
    public string Summary => "Show index and vector DB status";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.RequireProject(args, context);
        var metadata = context.Brain.LoadIndexMetadata(projectRoot);
        var stats = await context.Brain.GetVectorStatsAsync(projectRoot, CancellationToken.None);
        Console.WriteLine($"Project: {metadata?.ProjectName ?? Path.GetFileName(projectRoot)}");
        Console.WriteLine($"Path: {projectRoot}");
        Console.WriteLine($"Status: {metadata?.Status}");
        Console.WriteLine($"RAG mode: {metadata?.CurrentRagMode() ?? "No brain"}");
        Console.WriteLine($"Files: {metadata?.IndexedFiles ?? 0} indexed, {metadata?.SkippedFiles ?? 0} skipped");
        Console.WriteLine($"Chunks: {metadata?.TotalChunks ?? 0} total, {metadata?.EmbeddedChunks ?? 0} embedded");
        Console.WriteLine($"Vectors: {stats.VectorCount:N0} in {stats.CollectionName} (dim {stats.VectorDimension})");
        if (!string.IsNullOrWhiteSpace(metadata?.LastError))
        {
            Console.WriteLine($"Last error: {metadata.LastError}");
        }

        return 0;
    }
}
