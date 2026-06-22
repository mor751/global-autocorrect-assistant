namespace Autocorrect.Cli.Commands;

internal sealed class VectorsCommand : ICliCommand
{
    public string Name => "vectors";
    public string Summary => "Vector DB utilities (count, export paths)";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var sub = CliArgs.Positional(args, 0).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(sub))
        {
            sub = "count";
        }

        var projectRoot = CliArgs.RequireProject(args, context);
        if (sub is "count")
        {
            var stats = await context.Brain.GetVectorStatsAsync(projectRoot, CancellationToken.None);
            Console.WriteLine($"{stats.VectorCount:N0} vectors in {stats.CollectionName} (dim {stats.VectorDimension})");
            return 0;
        }

        if (sub is "export")
        {
            var points = await context.Brain.ExportVectorsAsync(projectRoot, CancellationToken.None);
            Console.WriteLine($"Exported {points.Count:N0} vectors");
            foreach (var point in points.Take(40))
            {
                Console.WriteLine($"{point.FilePath} :: {point.Symbol} ({point.ChunkType})");
            }

            if (points.Count > 40)
            {
                Console.WriteLine($"... and {points.Count - 40} more");
            }

            return 0;
        }

        throw new InvalidOperationException("Usage: woody vectors [count|export] [--path folder]");
    }
}
