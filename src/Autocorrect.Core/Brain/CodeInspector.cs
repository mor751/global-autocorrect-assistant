using System.Text.RegularExpressions;

namespace Autocorrect.Core.Brain;

// Lightweight, language-tolerant extraction of imports, exports, symbols, and a short summary.
public static partial class CodeInspector
{
    public static (List<string> Imports, List<string> Exports, List<string> Symbols) Inspect(string content)
    {
        var imports = ImportFrom().Matches(content).Select(m => m.Groups[1].Value)
            .Concat(RequireCall().Matches(content).Select(m => m.Groups[1].Value))
            .Concat(CsharpUsing().Matches(content).Select(m => m.Groups[1].Value))
            .Concat(PyImport().Matches(content).Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();

        var exports = ExportNamed().Matches(content).Select(m => m.Groups[1].Value)
            .Concat(ExportDefault().Matches(content).Select(_ => "default"))
            .Distinct(StringComparer.Ordinal)
            .Take(40)
            .ToList();

        var symbols = SymbolDecl().Matches(content).Select(m => m.Groups[1].Value)
            .Concat(CsharpType().Matches(content).Select(m => m.Groups[1].Value))
            .Concat(PyDef().Matches(content).Select(m => m.Groups[1].Value))
            .Where(s => s.Length > 1)
            .Distinct(StringComparer.Ordinal)
            .Take(40)
            .ToList();

        return (imports, exports, symbols);
    }

    // Picks a leading doc comment if present, otherwise derives a summary from role and key symbols.
    public static string Summarize(FileRole role, IReadOnlyList<string> symbols, string content)
    {
        var lead = LeadingComment(content);
        if (!string.IsNullOrWhiteSpace(lead))
        {
            return Truncate(lead, 160);
        }

        var top = symbols.Take(3).ToList();
        var roleText = role switch
        {
            FileRole.Component => "UI component",
            FileRole.Route => "App route/page",
            FileRole.Hook => "Reusable hook",
            FileRole.Api => "API/server handler",
            FileRole.Util => "Utility module",
            FileRole.Style => "Stylesheet",
            FileRole.Config => "Configuration",
            FileRole.Database => "Data/schema module",
            FileRole.Docs => "Documentation",
            FileRole.Test => "Test file",
            _ => "Source file"
        };

        return top.Count > 0 ? $"{roleText}: {string.Join(", ", top)}" : roleText;
    }

    public static IReadOnlyList<string> Chunk(string content, int chunkSize = 900, int maxChunks = 6)
    {
        var normalized = content.Replace("\r\n", "\n").Trim();
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var chunks = new List<string>();
        for (var i = 0; i < normalized.Length && chunks.Count < maxChunks; i += chunkSize)
        {
            chunks.Add(normalized.Substring(i, Math.Min(chunkSize, normalized.Length - i)));
        }

        return chunks;
    }

    private static string LeadingComment(string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("/**", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal))
        {
            var end = trimmed.IndexOf("*/", StringComparison.Ordinal);
            if (end > 0)
            {
                return CleanComment(trimmed[..end]);
            }
        }

        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return CleanComment(trimmed.Split('\n')[0]);
        }

        return string.Empty;
    }

    private static string CleanComment(string raw)
    {
        return string.Join(' ', raw
            .Replace("/**", string.Empty)
            .Replace("/*", string.Empty)
            .Replace("*/", string.Empty)
            .Replace("//", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('*', ' ')));
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    [GeneratedRegex(@"import\s+[^;]*?from\s+['""]([^'""]+)['""]")]
    private static partial Regex ImportFrom();

    [GeneratedRegex(@"require\(\s*['""]([^'""]+)['""]\s*\)")]
    private static partial Regex RequireCall();

    [GeneratedRegex(@"using\s+([A-Za-z0-9_.]+)\s*;")]
    private static partial Regex CsharpUsing();

    [GeneratedRegex(@"(?m)^\s*(?:from\s+([A-Za-z0-9_.]+)\s+import|import\s+([A-Za-z0-9_.]+))")]
    private static partial Regex PyImport();

    [GeneratedRegex(@"export\s+(?:async\s+)?(?:const|function|class|interface|type|enum)\s+([A-Za-z0-9_]+)")]
    private static partial Regex ExportNamed();

    [GeneratedRegex(@"export\s+default")]
    private static partial Regex ExportDefault();

    [GeneratedRegex(@"(?:function|const|class)\s+([A-Za-z_][A-Za-z0-9_]*)\s*[=(<]")]
    private static partial Regex SymbolDecl();

    [GeneratedRegex(@"(?:class|interface|record|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex CsharpType();

    [GeneratedRegex(@"(?m)^\s*def\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(")]
    private static partial Regex PyDef();
}
