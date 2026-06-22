using System.Text.Json;

namespace Autocorrect.Core.Brain;

public enum CodingAgentKind
{
    Generic,
    Cursor,
    Codex,
    ClaudeCode,
    VsCode
}

public sealed class IdeSessionContext
{
    public string ProcessName { get; init; } = string.Empty;
    public string WindowTitle { get; init; } = string.Empty;
    public CodingAgentKind Agent { get; init; } = CodingAgentKind.Generic;
    public string? WorkspaceRoot { get; init; }
    public string ResolutionSource { get; init; } = string.Empty;

    public PromptTargetAgent TargetAgent => Agent switch
    {
        CodingAgentKind.Cursor => PromptTargetAgent.Cursor,
        CodingAgentKind.Codex => PromptTargetAgent.Codex,
        CodingAgentKind.ClaudeCode => PromptTargetAgent.ClaudeCode,
        _ => PromptTargetAgent.Codex
    };
}

// Detects the active IDE workspace from Cursor/VS Code state, title, and saved sessions.
public static class ActiveIdeResolver
{
    private static readonly string[] IdeProcesses =
    [
        "Cursor",
        "Code",
        "devenv",
        "rider64",
        "idea64",
        "Windsurf"
    ];

    private static readonly (string Suffix, CodingAgentKind Agent)[] TitleSuffixes =
    [
        (" - Cursor", CodingAgentKind.Cursor),
        (" - Visual Studio Code", CodingAgentKind.VsCode),
        (" - Code", CodingAgentKind.VsCode),
        (" - Windsurf", CodingAgentKind.Cursor)
    ];

    private static readonly string[] IdeAppFolders = ["Cursor", "Code", "Windsurf"];

    public static IdeSessionContext Resolve(
        string processName,
        string windowTitle,
        string? fallbackProjectRoot,
        IReadOnlyList<string> indexedProjectRoots)
    {
        var agent = DetectAgent(processName, windowTitle);
        var stored = LoadStoredWorkspaces();
        var label = ParseWorkspaceLabel(windowTitle, processName);
        var inIde = IsIdeProcess(processName);
        var resolved = ResolveWorkspacePath(processName, label, stored, indexedProjectRoots, inIde ? null : fallbackProjectRoot);

        return new IdeSessionContext
        {
            ProcessName = processName,
            WindowTitle = windowTitle,
            Agent = agent,
            WorkspaceRoot = resolved.Path,
            ResolutionSource = resolved.Source
        };
    }

    public static bool IsIdeProcess(string processName) =>
        IdeProcesses.Any(name => name.Equals(processName, StringComparison.OrdinalIgnoreCase));

    private static CodingAgentKind DetectAgent(string processName, string windowTitle)
    {
        if (windowTitle.Contains("codex", StringComparison.OrdinalIgnoreCase))
        {
            return CodingAgentKind.Codex;
        }

        if (windowTitle.Contains("claude", StringComparison.OrdinalIgnoreCase))
        {
            return CodingAgentKind.ClaudeCode;
        }

        if (processName.Equals("Cursor", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("Windsurf", StringComparison.OrdinalIgnoreCase))
        {
            return CodingAgentKind.Cursor;
        }

        if (processName.Equals("Code", StringComparison.OrdinalIgnoreCase))
        {
            return CodingAgentKind.VsCode;
        }

        return CodingAgentKind.Generic;
    }

    private static string? ParseWorkspaceLabel(string title, string processName)
    {
        if (string.IsNullOrWhiteSpace(title) || !IsIdeProcess(processName))
        {
            return null;
        }

        foreach (var (suffix, _) in TitleSuffixes)
        {
            if (!title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var body = title[..^suffix.Length].Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var split = body.LastIndexOf(" - ", StringComparison.Ordinal);
            return split >= 0 ? body[(split + 3)..].Trim() : body;
        }

        return null;
    }

    private static (string? Path, string Source) ResolveWorkspacePath(
        string processName,
        string? label,
        IReadOnlyList<StoredWorkspace> stored,
        IReadOnlyList<string> indexedProjectRoots,
        string? fallbackProjectRoot)
    {
        foreach (var appFolder in ResolveAppFolders(processName))
        {
            var active = ReadLastActiveFolder(appFolder);
            if (!string.IsNullOrWhiteSpace(active) && Directory.Exists(active))
            {
                return (active, "ide-active-window");
            }
        }

        var hotStorage = stored
            .Where(item => item.LastOpenedUtc >= DateTime.UtcNow.AddHours(-6))
            .OrderByDescending(item => item.LastOpenedUtc)
            .Select(item => item.Root)
            .FirstOrDefault(path => Directory.Exists(path));
        if (!string.IsNullOrWhiteSpace(hotStorage))
        {
            return (hotStorage, "ide-recent-storage");
        }

        if (!string.IsNullOrWhiteSpace(label))
        {
            var byLabel = stored
                .Where(item => WorkspaceName(item.Root).Equals(label, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.LastOpenedUtc)
                .Select(item => item.Root)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (byLabel.Count == 1)
            {
                return (byLabel[0], "ide-storage");
            }

            if (byLabel.Count > 1)
            {
                return (byLabel[0], "ide-storage-recent");
            }

            var indexed = indexedProjectRoots
                .Where(path => WorkspaceName(path).Equals(label, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (indexed.Count == 1)
            {
                return (indexed[0], "indexed-project");
            }

            var contains = stored
                .Where(item => item.Root.Contains(label, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.LastOpenedUtc)
                .Select(item => item.Root)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(contains))
            {
                return (contains, "ide-storage-fuzzy");
            }
        }

        var sessionRecent = ProjectSessionRegistry.GetMostRecent();
        if (!string.IsNullOrWhiteSpace(sessionRecent) && IsIdeProcess(processName))
        {
            return (sessionRecent, "last-session");
        }

        if (!string.IsNullOrWhiteSpace(fallbackProjectRoot) && Directory.Exists(fallbackProjectRoot))
        {
            return (ProjectBrainService.NormalizeProjectRoot(fallbackProjectRoot), "settings");
        }

        return (null, string.Empty);
    }

    private static IEnumerable<string> ResolveAppFolders(string processName)
    {
        if (processName.Equals("Code", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Code";
            yield break;
        }

        if (processName.Equals("Windsurf", StringComparison.OrdinalIgnoreCase))
        {
            yield return "Windsurf";
            yield break;
        }

        if (processName.Equals("Cursor", StringComparison.OrdinalIgnoreCase) || IsIdeProcess(processName))
        {
            foreach (var folder in IdeAppFolders)
            {
                yield return folder;
            }
        }
    }

    private static string? ReadLastActiveFolder(string appFolder)
    {
        var storagePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            appFolder,
            "User",
            "globalStorage",
            "storage.json");
        if (!File.Exists(storagePath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(storagePath));
            if (document.RootElement.TryGetProperty("windowsState", out var windowsState) &&
                windowsState.TryGetProperty("lastActiveWindow", out var lastActive) &&
                lastActive.TryGetProperty("folder", out var folderElement))
            {
                var fromWindow = UriToPath(folderElement.GetString());
                if (!string.IsNullOrWhiteSpace(fromWindow))
                {
                    return fromWindow;
                }
            }

            if (document.RootElement.TryGetProperty("backupWorkspaces", out var backup) &&
                backup.TryGetProperty("folders", out var folders) &&
                folders.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in folders.EnumerateArray())
                {
                    if (!item.TryGetProperty("folderUri", out var uriElement))
                    {
                        continue;
                    }

                    var fromBackup = UriToPath(uriElement.GetString());
                    if (!string.IsNullOrWhiteSpace(fromBackup))
                    {
                        return fromBackup;
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static List<StoredWorkspace> LoadStoredWorkspaces()
    {
        var workspaces = new List<StoredWorkspace>();
        foreach (var appFolder in IdeAppFolders)
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appFolder, "User", "workspaceStorage");
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (directory.EndsWith("empty-window", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var jsonPath = Path.Combine(directory, "workspace.json");
                if (!File.Exists(jsonPath))
                {
                    continue;
                }

                var path = TryReadWorkspacePath(jsonPath);
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    continue;
                }

                workspaces.Add(new StoredWorkspace
                {
                    Root = ProjectBrainService.NormalizeProjectRoot(path),
                    App = appFolder,
                    LastOpenedUtc = Directory.GetLastWriteTimeUtc(directory)
                });
            }
        }

        return workspaces
            .GroupBy(item => item.Root, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.LastOpenedUtc).First())
            .ToList();
    }

    private static string? TryReadWorkspacePath(string jsonPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(jsonPath));
            if (!document.RootElement.TryGetProperty("folder", out var folder))
            {
                return null;
            }

            var uri = folder.GetString();
            return string.IsNullOrWhiteSpace(uri) ? null : UriToPath(uri);
        }
        catch
        {
            return null;
        }
    }

    private static string? UriToPath(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
        {
            return null;
        }

        return ProjectBrainService.NormalizeProjectRoot(parsed.LocalPath);
    }

    private static string WorkspaceName(string path) =>
        Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private sealed class StoredWorkspace
    {
        public string Root { get; init; } = string.Empty;
        public string App { get; init; } = string.Empty;
        public DateTime LastOpenedUtc { get; init; }
    }
}
