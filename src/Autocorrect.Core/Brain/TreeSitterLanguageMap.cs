namespace Autocorrect.Core.Brain;

public static class TreeSitterLanguageMap
{
    public static bool TryResolve(string extension, string relativePath, out string languageName, out string languageKey)
    {
        languageKey = LanguageFor(extension, relativePath);
        languageName = languageKey switch
        {
            "typescript" when extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase) => "TSX",
            "typescript" => "TypeScript",
            "javascript" => "JavaScript",
            "python" => "Python",
            "csharp" => "CSharp",
            "go" => "Go",
            "rust" => "Rust",
            "java" => "Java",
            "ruby" => "Ruby",
            "php" => "PHP",
            "swift" => "Swift",
            "kotlin" => "Java",
            "scala" => "Scala",
            "bash" => "Bash",
            "json" => "JSON",
            "html" => "HTML",
            "css" => "CSS",
            "toml" => "TOML",
            "sql" => "CodeQL",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(languageName);
    }

    private static string LanguageFor(string extension, string path) => extension.ToLowerInvariant() switch
    {
        ".ts" or ".tsx" => "typescript",
        ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
        ".py" => "python",
        ".cs" => "csharp",
        ".go" => "go",
        ".rs" => "rust",
        ".java" => "java",
        ".rb" => "ruby",
        ".php" => "php",
        ".swift" => "swift",
        ".kt" => "kotlin",
        ".scala" => "scala",
        ".sh" or ".bash" => "bash",
        ".json" => "json",
        ".html" or ".htm" => "html",
        ".css" or ".scss" => "css",
        ".toml" => "toml",
        ".sql" => "sql",
        _ when Path.GetFileName(path).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) => string.Empty,
        _ => string.Empty
    };
}
