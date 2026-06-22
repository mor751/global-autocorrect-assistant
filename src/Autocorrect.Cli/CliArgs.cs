using Autocorrect.Core.Brain;

namespace Autocorrect.Cli;

internal static class CliArgs
{
    private static readonly HashSet<string> CommandTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "count", "export"
    };

    public static string? Flag(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    public static int IntFlag(string[] args, string name, int fallback)
    {
        var raw = Flag(args, name);
        return int.TryParse(raw, out var value) ? value : fallback;
    }

    public static string Positional(string[] args, int index) =>
        index < args.Length ? args[index] : string.Empty;

    public static string RequireProject(string[] args, CliContext context)
    {
        var path = Flag(args, "--path") ?? args.FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal) && !CommandTokens.Contains(arg));
        if (!string.IsNullOrWhiteSpace(path))
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                throw new InvalidOperationException($"Folder not found: {fullPath}");
            }

            return fullPath;
        }

        var active = ActiveProjectStore.Get(context.Settings);
        if (!string.IsNullOrWhiteSpace(active) && Directory.Exists(active))
        {
            return Path.GetFullPath(active);
        }

        if (!string.IsNullOrWhiteSpace(context.ProjectRoot))
        {
            var fullPath = Path.GetFullPath(context.ProjectRoot);
            if (!Directory.Exists(fullPath))
            {
                throw new InvalidOperationException($"Folder not found: {fullPath} (from settings.json ProjectRoot)");
            }

            return fullPath;
        }

        throw new InvalidOperationException("No project path. Pass --path, set active project in Woody dashboard, or set ProjectRoot in settings.json.");
    }

    public static string RequireText(string[] args, string flagName)
    {
        var text = Flag(args, flagName) ?? string.Join(' ', args.Where(arg => !arg.StartsWith("-", StringComparison.Ordinal)));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Missing text. Example: woody search --query \"fix login bug\"");
        }

        return text.Trim();
    }
}
