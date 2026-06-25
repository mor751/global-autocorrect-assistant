using System.Diagnostics;
using System.Text.Json;
using Autocorrect.Core;
using Autocorrect.Core.Brain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Autocorrect.Cli.Brain;

internal sealed class BrainHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly CliContext _context;
    private readonly string _projectRoot;
    private readonly int _port;
    private readonly bool _openBrowser;

    public BrainHost(CliContext context, string projectRoot, int port, bool openBrowser)
    {
        _context = context;
        _projectRoot = projectRoot;
        _port = port;
        _openBrowser = openBrowser;
    }

    // Serves the vector + symbol brain UI on localhost until Ctrl+C.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var webRoot = ResolveWebRoot();
        if (!Directory.Exists(webRoot))
        {
            Console.Error.WriteLine($"Brain UI assets not found at: {webRoot}");
            return 1;
        }

        var cache = new BrainSnapshotCache(_context, _projectRoot);
        Console.WriteLine("Brain: warming snapshot…");
        await cache.WarmAsync(cancellationToken);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.WebHost.UseUrls($"http://127.0.0.1:{_port}");
        var app = builder.Build();
        var fileProvider = new PhysicalFileProvider(webRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });

        app.MapGet("/api/status", async () =>
            Results.Json(await cache.GetStatusAsync(cancellationToken), JsonOptions));

        app.MapGet("/api/vectors", async () =>
            Results.Json(await cache.GetVectorMapAsync(cancellationToken), JsonOptions));

        app.MapGet("/api/graph", async () =>
            Results.Json(await cache.GetSymbolGraphAsync(cancellationToken), JsonOptions));

        app.MapPost("/api/reload", async () =>
        {
            var force = false;
            var report = await _context.Brain.SyncAsync(_projectRoot, force, cancellationToken);
            await cache.RefreshAsync(cancellationToken);
            return Results.Json(new
            {
                ok = true,
                ready = cache.IsReady,
                syncPerformed = report.SyncPerformed,
                message = report.Message,
                changes = report.AddedFiles + report.ModifiedFiles + report.RemovedFiles
            }, JsonOptions);
        });

        app.MapPost("/api/search", async (BrainWebSearchRequest request) =>
        {
            var topK = request.TopK <= 0 ? _context.Settings.RetrievalTopK : request.TopK;
            var response = await _context.Brain.SearchDetailedAsync(request.Query, _projectRoot, topK, cancellationToken);
            return Results.Json(response, JsonOptions);
        });

        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fileProvider });

        var url = $"http://127.0.0.1:{_port}";
        Console.WriteLine("Woody Second Brain");
        Console.WriteLine($"Project: {_projectRoot}");
        Console.WriteLine($"Open:    {url}");
        Console.WriteLine("Press Ctrl+C to stop.");

        if (_openBrowser)
        {
            TryOpenBrowser(url);
        }

        await app.RunAsync(cancellationToken);
        return 0;
    }

    private static string ResolveWebRoot()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"))
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            Console.WriteLine($"Could not open browser automatically. Visit {url}");
        }
    }
}
