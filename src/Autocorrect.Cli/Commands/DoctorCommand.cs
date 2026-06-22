namespace Autocorrect.Cli.Commands;

internal sealed class DoctorCommand : ICliCommand
{
    public string Name => "doctor";
    public string Summary => "Health check for embedder, vector DB, Ollama, and index";

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
        Console.WriteLine("Woody doctor");
        Console.WriteLine($"Project indexed: {report.ProjectIndexed}");
        if (!string.IsNullOrWhiteSpace(report.ProjectRoot))
        {
            Console.WriteLine($"Project root: {report.ProjectRoot}");
        }

        Console.WriteLine($"Brain status: {report.Status}");
        Console.WriteLine($"RAG mode: {report.RagMode}");
        Console.WriteLine($"Embedder: {(report.EmbedderReady ? "ready" : "unavailable")} ({report.EmbedderDimension} dim, downloaded={report.EmbedderDownloaded})");
        if (!string.IsNullOrWhiteSpace(report.EmbedderError))
        {
            Console.WriteLine($"Embedder error: {report.EmbedderError}");
        }

        Console.WriteLine($"Vector DB: {(report.VectorStoreReady ? "ready" : "unavailable")} ({report.VectorCount:N0} vectors)");
        Console.WriteLine($"Symbol graph: {report.SymbolNodes:N0} nodes, {report.SymbolEdges:N0} edges");
        Console.WriteLine($"Ollama: {(report.OllamaAvailable ? "online" : "offline")} ({report.OllamaModel})");
        Console.WriteLine($"Indexed files: {report.IndexedFiles}, chunks: {report.TotalChunks}, embedded: {report.EmbeddedChunks}, skipped: {report.SkippedFiles}");
        if (!string.IsNullOrWhiteSpace(report.LastError))
        {
            Console.WriteLine($"Last error: {report.LastError}");
        }

        return report.EmbedderReady && report.VectorStoreReady ? 0 : 2;
    }
}
