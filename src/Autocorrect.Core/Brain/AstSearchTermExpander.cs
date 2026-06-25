namespace Autocorrect.Core.Brain;

public static class AstSearchTermExpander
{
    public static IReadOnlyList<string> Expand(string query, PromptSymbolParseResult parsed)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokenize(query))
        {
            terms.Add(token);
            if (token.Length >= 2)
            {
                terms.Add(char.ToUpperInvariant(token[0]) + token[1..]);
            }
        }

        foreach (var symbol in parsed.Symbols)
        {
            terms.Add(symbol);
        }

        foreach (var type in parsed.TypeNames)
        {
            terms.Add(type);
        }

        foreach (var method in parsed.MethodNames)
        {
            terms.Add(method);
        }

        return terms
            .Where(term => term.Length >= 2)
            .OrderByDescending(term => term.Length)
            .Take(16)
            .ToList();
    }

    private static IEnumerable<string> Tokenize(string text) =>
        text
            .Split(new[] { ' ', '/', '\\', '.', '-', '_', ',', ':', ';', '(', ')', '\n', '\t', '"', '\'', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2)
            .Select(token => token.Trim());
}
