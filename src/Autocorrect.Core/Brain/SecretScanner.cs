using System.Text.RegularExpressions;

namespace Autocorrect.Core.Brain;

// Redacts secrets (keys, tokens, db urls, passwords) before any text enters a context pack or prompt.
public static partial class SecretScanner
{
    private const string Mask = "[REDACTED]";

    private static readonly Regex[] Patterns =
    {
        AssignmentSecret(),
        BearerToken(),
        PrivateKeyBlock(),
        DatabaseUrl(),
        ApiKeyLike(),
        JwtLike()
    };

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var pattern in Patterns)
        {
            text = pattern.Replace(text, Mask);
        }

        return text;
    }

    public static bool LooksLikeSecretFile(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return name.StartsWith(".env", StringComparison.Ordinal) ||
               name.Contains("secret", StringComparison.Ordinal) ||
               name.Contains("credential", StringComparison.Ordinal) ||
               name.EndsWith(".pem", StringComparison.Ordinal) ||
               name.EndsWith(".key", StringComparison.Ordinal) ||
               name.EndsWith(".pfx", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?im)^\s*(?:export\s+)?[A-Z0-9_]*(?:KEY|TOKEN|SECRET|PASSWORD|PASSWD|PWD|AUTH)[A-Z0-9_]*\s*[:=]\s*[""']?[^\s""']+[""']?")]
    private static partial Regex AssignmentSecret();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----")]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"(?i)\b(?:postgres(?:ql)?|mysql|mongodb(?:\+srv)?|redis|amqp)://[^\s""']+")]
    private static partial Regex DatabaseUrl();

    [GeneratedRegex(@"\b(?:sk|pk|rk|api|ghp|gho|xox[baprs])[-_][A-Za-z0-9]{12,}\b")]
    private static partial Regex ApiKeyLike();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b")]
    private static partial Regex JwtLike();
}
