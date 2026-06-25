namespace Autocorrect.Cli.Commands;

internal sealed class SearchCommand : ICliCommand
{
    public string Name => "search";
    public string Summary => "Semantic + graph search over the project brain";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.ResolveProject(args, context);
        var query = CliArgs.RequireQueryText(args);
        var engine = CliArgs.ParseRetrievalEngine(args);
        var topK = CliArgs.IntFlag(args, "--top", context.Settings.RetrievalTopK);
        var response = await context.Brain.SearchDetailedAsync(query, projectRoot, topK, CancellationToken.None, engine);
        Console.WriteLine($"Engine: {CliArgs.DescribeEngine(engine)}");
        Console.WriteLine($"Mode: {response.RetrievalMode}");
        Console.WriteLine($"Query: {response.Query}");
        Console.WriteLine();
        var rank = 1;
        foreach (var result in response.Results)
        {
            var symbol = string.IsNullOrWhiteSpace(result.Symbol) ? "-" : result.Symbol;
            Console.WriteLine($"{rank,2}. [{result.Score:0.000}] {result.FilePath} :: {symbol} ({result.Reason})");
            Console.WriteLine($"    {result.ContentPreview}");
            rank++;
        }

        return 0;
    }
}
