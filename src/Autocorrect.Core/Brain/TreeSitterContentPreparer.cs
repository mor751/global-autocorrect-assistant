using System.Text;
using System.Text.RegularExpressions;

namespace Autocorrect.Core.Brain;

public static partial class TreeSitterContentPreparer
{
    public static PreparedTreeSitterContent? Prepare(string relativePath, string content)
    {
        var extension = Path.GetExtension(relativePath);
        if (extension.Equals(".razor", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase))
        {
            return new PreparedTreeSitterContent(ExtractRazorCSharp(content), "csharp", "CSharp");
        }

        if (extension.Equals(".vue", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".svelte", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".astro", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryExtractScript(content, out var script, out var typescript))
            {
                return null;
            }

            return typescript
                ? new PreparedTreeSitterContent(script, "typescript", "TypeScript")
                : new PreparedTreeSitterContent(script, "javascript", "JavaScript");
        }

        if (!TreeSitterLanguageMap.TryResolve(extension, relativePath, out var languageName, out var languageKey))
        {
            return null;
        }

        return new PreparedTreeSitterContent(content, languageKey, languageName);
    }

    private static string ExtractRazorCSharp(string content)
    {
        var builder = new StringBuilder();
        foreach (Match match in RazorCodeBlockRegex().Matches(content))
        {
            builder.AppendLine(match.Groups["code"].Value);
        }

        foreach (Match match in RazorInjectRegex().Matches(content))
        {
            builder.AppendLine(match.Value);
        }

        if (builder.Length == 0)
        {
            return content;
        }

        return builder.ToString();
    }

    private static bool TryExtractScript(string content, out string script, out bool typescript)
    {
        script = string.Empty;
        typescript = false;
        var match = ScriptTagRegex().Match(content);
        if (!match.Success)
        {
            return false;
        }

        var attrs = match.Groups["attrs"].Value;
        typescript = attrs.Contains("lang=\"ts\"", StringComparison.OrdinalIgnoreCase) ||
                     attrs.Contains("lang='ts'", StringComparison.OrdinalIgnoreCase) ||
                     attrs.Contains("lang=\"typescript\"", StringComparison.OrdinalIgnoreCase);
        script = match.Groups["body"].Value.Trim();
        return script.Length > 0;
    }

    [GeneratedRegex(@"@code\s*\{(?<code>[\s\S]*?)\}", RegexOptions.Compiled)]
    private static partial Regex RazorCodeBlockRegex();

    [GeneratedRegex(@"@\{(?<code>[\s\S]*?)\}", RegexOptions.Compiled)]
    private static partial Regex RazorInjectRegex();

    [GeneratedRegex(@"<script\b(?<attrs>[^>]*)>(?<body>[\s\S]*?)</script>", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();
}

public sealed record PreparedTreeSitterContent(string Content, string LanguageKey, string LanguageName);
