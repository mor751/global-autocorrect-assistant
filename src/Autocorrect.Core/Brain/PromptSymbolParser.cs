using System.Text.RegularExpressions;

namespace Autocorrect.Core.Brain;

// Pulls likely code symbols, file paths, and qualified names out of a natural-language prompt.
public static partial class PromptSymbolParser
{
    public static PromptSymbolParseResult Parse(string prompt)
    {
        var result = new PromptSymbolParseResult { OriginalPrompt = prompt.Trim() };
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return result;
        }

        foreach (Match match in FilePathRegex().Matches(prompt))
        {
            result.FilePaths.Add(NormalizePath(match.Value));
        }

        foreach (Match match in QualifiedNameRegex().Matches(prompt))
        {
            var type = match.Groups["type"].Value;
            var method = match.Groups["method"].Value;
            if (!string.IsNullOrWhiteSpace(type))
            {
                result.TypeNames.Add(type);
                result.Symbols.Add(type);
            }

            if (!string.IsNullOrWhiteSpace(method))
            {
                result.MethodNames.Add(method);
                result.Symbols.Add(method);
            }
        }

        foreach (Match match in PascalCaseRegex().Matches(prompt))
        {
            var value = match.Value;
            if (IsNoise(value))
            {
                continue;
            }

            result.Symbols.Add(value);
            if (char.IsUpper(value[0]))
            {
                result.TypeNames.Add(value);
            }
        }

        foreach (Match match in CamelCaseRegex().Matches(prompt))
        {
            var value = match.Value;
            if (IsNoise(value))
            {
                continue;
            }

            result.MethodNames.Add(value);
            result.Symbols.Add(value);
        }

        result.Symbols = result.Symbols.Distinct(StringComparer.Ordinal).ToList();
        result.TypeNames = result.TypeNames.Distinct(StringComparer.Ordinal).ToList();
        result.MethodNames = result.MethodNames.Distinct(StringComparer.Ordinal).ToList();
        result.FilePaths = result.FilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return result;
    }

    private static bool IsNoise(string value)
    {
        if (value.Length < 3)
        {
            return true;
        }

        var lower = value.ToLowerInvariant();
        return lower is "the" or "and" or "for" or "fix" or "bug" or "add" or "make" or "use" or "not" or "api" or "sql" or "rag";
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim();

    [GeneratedRegex(@"\b(?<type>[A-Z][A-Za-z0-9_]*)\.(?<method>[A-Z][A-Za-z0-9_]*)\b", RegexOptions.Compiled)]
    private static partial Regex QualifiedNameRegex();

    [GeneratedRegex(@"\b[A-Z][A-Za-z0-9_]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex PascalCaseRegex();

    [GeneratedRegex(@"\b[a-z][A-Za-z0-9_]{3,}\b", RegexOptions.Compiled)]
    private static partial Regex CamelCaseRegex();

    [GeneratedRegex(@"(?:[\w.-]+[/\\])+[\w.-]+\.(?:cs|ts|tsx|js|jsx|py|go|rs|java|md|json|xaml|sql)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex FilePathRegex();
}
