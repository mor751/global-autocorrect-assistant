namespace Autocorrect.Core.Brain;

public sealed class ArchitectureEntryPoint
{
    public string FilePath { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public double Score { get; set; }
}

public sealed class ArchitectureCommunity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Layer { get; set; } = "core";
    public List<string> FilePaths { get; set; } = new();
}

// Detects likely app entry files so vague prompts still land near the root flow.
public static class EntryPointIndexer
{
    private static readonly (string Pattern, string Kind, double Score)[] Rules =
    [
        ("program.cs", "dotnet-main", 1.0),
        ("app.xaml.cs", "wpf-app", 0.98),
        ("startup.cs", "aspnet-startup", 0.96),
        ("main.rs", "rust-main", 0.96),
        ("main.go", "go-main", 0.96),
        ("index.ts", "node-entry", 0.9),
        ("index.js", "node-entry", 0.88),
        ("main.ts", "node-entry", 0.88),
        ("server.ts", "server-entry", 0.9),
        ("app/page.tsx", "next-app-router", 0.95),
        ("pages/_app.tsx", "next-pages", 0.94),
        ("pages/index.tsx", "next-home", 0.92),
        ("app.tsx", "react-root", 0.9),
        ("__init__.py", "python-package", 0.75),
        ("manage.py", "django-entry", 0.94)
    ];

    public static List<ArchitectureEntryPoint> Detect(ProjectBrainData brain)
    {
        var points = new List<ArchitectureEntryPoint>();
        foreach (var file in brain.Files)
        {
            var lowerPath = file.Path.Replace('\\', '/').ToLowerInvariant();
            var lowerName = Path.GetFileName(lowerPath);
            foreach (var (pattern, kind, score) in Rules)
            {
                if (!lowerPath.EndsWith(pattern, StringComparison.Ordinal) && lowerName != pattern)
                {
                    continue;
                }

                points.Add(new ArchitectureEntryPoint
                {
                    FilePath = file.Path,
                    Label = Path.GetFileName(file.Path),
                    Kind = kind,
                    Score = score + file.Importance * 0.05
                });
                break;
            }

            if (file.Role is FileRole.Route && lowerName.StartsWith("page.", StringComparison.Ordinal))
            {
                points.Add(new ArchitectureEntryPoint
                {
                    FilePath = file.Path,
                    Label = file.Path,
                    Kind = "route-page",
                    Score = 0.86 + file.Importance * 0.05
                });
            }
        }

        return points
            .GroupBy(point => point.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(point => point.Score).First())
            .OrderByDescending(point => point.Score)
            .Take(16)
            .ToList();
    }
}
