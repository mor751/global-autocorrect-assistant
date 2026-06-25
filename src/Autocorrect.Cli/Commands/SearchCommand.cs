namespace Autocorrect.Cli.Commands;

internal sealed class SearchCommand : ICliCommand
{
    public string Name => "search";
    public string Summary => "Search the brain for files, symbols, and line regions";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        var projectRoot = CliArgs.ResolveProject(args, context);
        var query = CliArgs.RequireQueryText(args);
        var engine = CliArgs.ParseRetrievalEngine(args);
        var topK = CliArgs.IntFlag(args, "--top", context.Settings.RetrievalTopK);

        WoodyConsole.WriteBrandBanner();
        WoodyConsole.WriteCommandHeader("search", "Find exact files and line regions in the project brain.");
        WoodyConsole.WriteMeta("Query", query);
        WoodyConsole.WriteMeta("Engine", CliArgs.DescribeEngine(engine));
        WoodyConsole.WriteMeta("Project", Path.GetFileName(projectRoot));
        Console.WriteLine();

        var response = await context.Brain.SearchDetailedAsync(query, projectRoot, topK, CancellationToken.None, engine);
        if (response.Results.Count == 0)
        {
            WoodyConsole.WriteWarn("No matches. Try woody index or woody reload --force.");
            return 1;
        }

        WoodyConsole.WriteDivider($"RESULTS · {response.RetrievalMode}");
        var rank = 1;
        foreach (var result in response.Results)
        {
            WoodyConsole.WriteRankedHit(rank++, new RetrievalHitView(
                result.FilePath,
                result.Symbol,
                result.Score,
                result.Reason,
                result.ContentPreview,
                result.StartLine,
                result.EndLine));
        }

        Console.WriteLine();
        WoodyConsole.WriteSuccess($"{response.Results.Count} hits · use woody prompt for a compiled agent prompt.");
        Console.WriteLine();
        return 0;
    }
}
