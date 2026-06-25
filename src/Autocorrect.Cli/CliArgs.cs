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

    public static string RequireProject(string[] args, CliContext context) =>
        ResolveProject(args, context, allowPositionalFolder: true);

    public static string ResolveProject(string[] args, CliContext context, bool allowPositionalFolder = false)
    {
        var path = Flag(args, "--path");
        if (!string.IsNullOrWhiteSpace(path))
        {
            return RequireExistingFolder(path);
        }

        if (allowPositionalFolder)
        {
            foreach (var candidate in NonOptionArgs(args))
            {
                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        var active = ActiveProjectStore.Get(context.Settings);
        if (!string.IsNullOrWhiteSpace(active) && Directory.Exists(active))
        {
            return Path.GetFullPath(active);
        }

        if (!string.IsNullOrWhiteSpace(context.ProjectRoot))
        {
            return RequireExistingFolder(context.ProjectRoot);
        }

        throw new InvalidOperationException(
            allowPositionalFolder
                ? "No project path. Pass --path <folder>, a folder argument, or set the active project in Woody."
                : "No project path. Pass --path <folder> or set the active project in Woody dashboard.");
    }

    public static string RequirePromptText(string[] args)
    {
        var fromFlag = Flag(args, "--prompt");
        if (!string.IsNullOrWhiteSpace(fromFlag))
        {
            return fromFlag.Trim();
        }

        var text = string.Join(' ', NonOptionArgs(args));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Missing prompt text. Example: woody prompt find login bug");
        }

        return text.Trim();
    }

    public static string RequireQueryText(string[] args)
    {
        var fromFlag = Flag(args, "--query");
        return !string.IsNullOrWhiteSpace(fromFlag) ? fromFlag.Trim() : RequirePromptText(args);
    }

    public static RetrievalEnginePreference ParseRetrievalEngine(string[] args)
    {
        var rag = HasSwitch(args, "--rag");
        var ast = HasSwitch(args, "--ast");
        if (rag && ast)
        {
            throw new InvalidOperationException("Use only one engine flag: --rag or --ast.");
        }

        if (rag)
        {
            return RetrievalEnginePreference.Rag;
        }

        if (ast)
        {
            return RetrievalEnginePreference.Ast;
        }

        return RetrievalEnginePreference.Hybrid;
    }

    public static bool HasSwitch(string[] args, string name) =>
        args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static string DescribeEngine(RetrievalEnginePreference engine) => engine switch
    {
        RetrievalEnginePreference.Rag => "RAG only (vector semantic)",
        RetrievalEnginePreference.Ast => "AST only (symbol graph)",
        _ => "Hybrid (RAG + AST)"
    };

    public static IEnumerable<string> NonOptionArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("-", StringComparison.Ordinal))
            {
                yield return arg;
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                i++;
            }
        }
    }

    private static string RequireExistingFolder(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new InvalidOperationException($"Folder not found: {fullPath}");
        }

        return fullPath;
    }

    public static string RequireText(string[] args, string flagName)
    {
        var text = Flag(args, flagName) ?? string.Join(' ', NonOptionArgs(args));
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException($"Missing text. Example: woody search --query \"fix login bug\"");
        }

        return text.Trim();
    }
}
