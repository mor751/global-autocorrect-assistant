using System.Text.Json;

namespace Autocorrect.Core.Brain;

public static class ProjectSessionRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static void Record(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            return;
        }

        var normalized = ProjectBrainService.NormalizeProjectRoot(projectRoot);
        var sessions = Load();
        sessions[normalized] = new ProjectSessionEntry
        {
            ProjectRoot = normalized,
            LastUsedUtc = DateTimeOffset.UtcNow,
            ProjectName = Path.GetFileName(normalized)
        };
        Save(sessions);
    }

    public static string? GetMostRecent()
    {
        return Load()
            .Values
            .Where(entry => Directory.Exists(entry.ProjectRoot))
            .OrderByDescending(entry => entry.LastUsedUtc)
            .Select(entry => entry.ProjectRoot)
            .FirstOrDefault();
    }

    public static IReadOnlyList<ProjectSessionEntry> List() =>
        Load().Values.OrderByDescending(entry => entry.LastUsedUtc).ToList();

    private static Dictionary<string, ProjectSessionEntry> Load()
    {
        var path = StorePath();
        if (!File.Exists(path))
        {
            return new Dictionary<string, ProjectSessionEntry>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, ProjectSessionEntry>>(File.ReadAllText(path), JsonOptions)
                   ?? new Dictionary<string, ProjectSessionEntry>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, ProjectSessionEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save(Dictionary<string, ProjectSessionEntry> sessions)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(StorePath(), JsonSerializer.Serialize(sessions, JsonOptions));
        }
        catch
        {
        }
    }

    private static string StorePath() => Path.Combine(AppPaths.DataDirectory, "project-sessions.json");
}

public sealed class ProjectSessionEntry
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public DateTimeOffset LastUsedUtc { get; set; }
}
