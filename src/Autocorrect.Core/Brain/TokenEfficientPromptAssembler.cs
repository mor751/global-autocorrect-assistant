using System.Text;

namespace Autocorrect.Core.Brain;

public static class TokenEfficientPromptAssembler
{
    private const int MaxLineHits = 14;

    public static string Assemble(string compactPrompt, PromptCompilerRequest request, PromptTargetAgent agent)
    {
        var body = CompactPromptText(compactPrompt, request.OriginalPrompt);
        var lineHits = SelectLineHits(request.Retrieval.Results);
        if (lineHits.Count == 0)
        {
            return AgentTail(body, agent);
        }

        var builder = new StringBuilder(body.Trim());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Read at files:");
        foreach (var hit in lineHits)
        {
            builder.AppendLine(FormatLineHit(hit));
        }

        return AgentTail(builder.ToString().Trim(), agent);
    }

    private static string CompactPromptText(string body, string originalPrompt)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Clean(originalPrompt);
        }

        var flattened = body
            .Replace("\r\n", "\n")
            .Replace("Goal:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Context:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Steps:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Acceptance:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Constraints:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\n", " ");

        while (flattened.Contains("  ", StringComparison.Ordinal))
        {
            flattened = flattened.Replace("  ", " ", StringComparison.Ordinal);
        }

        return flattened.Trim();
    }

    private static List<RetrievalResult> SelectLineHits(IReadOnlyList<RetrievalResult> results)
    {
        var ordered = results
            .Where(result => !string.IsNullOrWhiteSpace(result.FilePath))
            .OrderByDescending(result => result.StartLine > 0 ? 1 : 0)
            .ThenByDescending(result => result.Score)
            .ThenBy(result => result.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.StartLine)
            .ToList();

        var picked = new List<RetrievalResult>();
        var perFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in ordered)
        {
            if (picked.Count >= MaxLineHits)
            {
                break;
            }

            var key = LineKey(result);
            if (!seen.Add(key))
            {
                continue;
            }

            if (perFile.GetValueOrDefault(result.FilePath) >= 2)
            {
                continue;
            }

            picked.Add(result);
            perFile[result.FilePath] = perFile.GetValueOrDefault(result.FilePath) + 1;
        }

        return picked;
    }

    private static string LineKey(RetrievalResult result)
    {
        if (result.StartLine > 0 && result.EndLine > 0)
        {
            return $"{result.FilePath}:{result.StartLine}-{result.EndLine}:{result.Symbol}";
        }

        return result.FilePath;
    }

    private static string FormatLineHit(RetrievalResult hit)
    {
        var label = string.IsNullOrWhiteSpace(hit.Symbol) ? hit.ChunkType : hit.Symbol;
        if (hit.StartLine > 0 && hit.EndLine > 0)
        {
            return $"- {hit.FilePath}:{hit.StartLine}-{hit.EndLine} ({label})";
        }

        return $"- {hit.FilePath} ({label})";
    }

    private static string AgentTail(string text, PromptTargetAgent agent) =>
        agent switch
        {
            PromptTargetAgent.Cursor => text,
            PromptTargetAgent.ClaudeCode => text + "\n\nRead the listed regions first. Keep changes minimal.",
            PromptTargetAgent.Codex => text + "\n\nRead the listed regions first. Keep scope tight.",
            _ => text
        };

    private static string Clean(string prompt) =>
        string.Join(' ', prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
