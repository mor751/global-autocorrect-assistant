namespace Autocorrect.Cli.Commands;

internal sealed class DoctorCommand : ICliCommand
{
    public string Name => "doctor";
    public string Summary => "Health check embedder, vectors, Ollama, and index";

    public async Task<int> RunAsync(string[] args, CliContext context)
    {
        string? projectRoot = null;
        try
        {
            projectRoot = CliArgs.RequireProject(args, context);
        }
        catch
        {
            projectRoot = context.ProjectRoot;
        }

        var report = await context.Brain.DoctorAsync(projectRoot, CancellationToken.None);
        WoodyConsole.WriteBrandBanner();
        WoodyConsole.WriteCommandHeader("doctor", "System health for Woody brain and prompt compiler.");
        Console.WriteLine();

        WriteCheck("Project indexed", report.ProjectIndexed);
        if (!string.IsNullOrWhiteSpace(report.ProjectRoot))
        {
            WoodyConsole.WriteMeta("Path", report.ProjectRoot);
        }

        WoodyConsole.WriteMeta("Brain status", report.Status.ToString());
        WoodyConsole.WriteMeta("RAG mode", report.RagMode);
        WriteCheck("Embedder", report.EmbedderReady, $"{report.EmbedderDimension} dim");
        if (!string.IsNullOrWhiteSpace(report.EmbedderError))
        {
            WoodyConsole.WriteWarn(report.EmbedderError);
        }

        WriteCheck("Vector DB", report.VectorStoreReady, $"{report.VectorCount:N0} vectors");
        WoodyConsole.WriteMeta("Symbol graph", $"{report.SymbolNodes:N0} nodes · {report.SymbolEdges:N0} edges");
        WriteCheck("Ollama", report.OllamaAvailable, report.OllamaModel);
        WoodyConsole.WriteMeta("Indexed", $"{report.IndexedFiles:N0} files · {report.TotalChunks:N0} chunks · {report.EmbeddedChunks:N0} embedded");
        if (!string.IsNullOrWhiteSpace(report.LastError))
        {
            WoodyConsole.WriteWarn(report.LastError);
        }

        Console.WriteLine();
        var healthy = report.EmbedderReady && report.VectorStoreReady;
        if (healthy)
        {
            WoodyConsole.WriteSuccess("Woody brain looks healthy.");
        }
        else
        {
            WoodyConsole.WriteWarn("Some components need attention. Run woody index or woody reload --force.");
        }

        Console.WriteLine();
        return healthy ? 0 : 2;
    }

    private static void WriteCheck(string label, bool ok, string? detail = null)
    {
        var value = ok ? "ok" : "missing";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            value += $" · {detail}";
        }

        if (ok)
        {
            WoodyConsole.WriteMeta(label, value);
            return;
        }

        WoodyConsole.WriteLabel($"  {label}", value);
    }
}
