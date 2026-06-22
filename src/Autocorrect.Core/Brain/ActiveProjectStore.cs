using System.Text.Json;

namespace Autocorrect.Core.Brain;

public sealed class ActiveProjectState
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; }
}

// Single source of truth for which folder Woody brain + CLI use right now.
public static class ActiveProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string? Get(CorrectionSettings? settings = null)
    {
        var fromFile = Load();
        if (!string.IsNullOrWhiteSpace(fromFile?.ProjectRoot) && Directory.Exists(fromFile.ProjectRoot))
        {
            return ProjectBrainService.NormalizeProjectRoot(fromFile.ProjectRoot);
        }

        if (!string.IsNullOrWhiteSpace(settings?.ProjectRoot) && Directory.Exists(settings.ProjectRoot))
        {
            return ProjectBrainService.NormalizeProjectRoot(settings.ProjectRoot);
        }

        return ProjectSessionRegistry.GetMostRecent();
    }

    public static void Set(string projectRoot, CorrectionSettings settings, Action<CorrectionSettings> saveSettings)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return;
        }

        var normalized = ProjectBrainService.NormalizeProjectRoot(projectRoot);
        settings.ProjectRoot = normalized;
        saveSettings(settings);
        ProjectSessionRegistry.Record(normalized);
        Save(new ActiveProjectState
        {
            ProjectRoot = normalized,
            ProjectName = Path.GetFileName(normalized),
            UpdatedUtc = DateTimeOffset.UtcNow
        });
    }

    public static ActiveProjectState? Load()
    {
        var path = StorePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ActiveProjectState>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void Save(ActiveProjectState state)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(StorePath(), JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
        }
    }

    private static string StorePath() => Path.Combine(AppPaths.DataDirectory, "active-project.json");
}
