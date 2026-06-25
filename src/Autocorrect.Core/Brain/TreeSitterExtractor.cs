using TreeSitter;

namespace Autocorrect.Core.Brain;

// Pass 1 structure extraction: tree-sitter parses code into symbol nodes and import/call edges (graphify-style).
public sealed class TreeSitterExtractor
{
    public GraphExtractionResult? TryExtract(string relativePath, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var prepared = TreeSitterContentPreparer.Prepare(relativePath, content);
        if (prepared is null)
        {
            return null;
        }

        try
        {
            using var language = new Language(prepared.LanguageName);
            using var parser = new Parser(language);
            using var tree = parser.Parse(prepared.Content);
            if (tree is null)
            {
                return null;
            }

            var result = new GraphExtractionResult
            {
                SourceFile = relativePath,
                Language = prepared.LanguageKey
            };

            var fileNodeId = $"file:{relativePath}";
            result.Nodes.Add(new ExtractionNode
            {
                Id = fileNodeId,
                Label = Path.GetFileName(relativePath),
                SourceFile = relativePath,
                Kind = "file",
                StartLine = 1,
                EndLine = CountLines(content),
                Content = content.Length > 4000 ? content[..4000] : content
            });

            CollectSymbols(language, tree.RootNode, prepared.Content, relativePath, prepared.LanguageKey, result);
            CollectImports(language, tree.RootNode, relativePath, prepared.LanguageKey, result, fileNodeId);
            CollectCalls(language, tree.RootNode, relativePath, prepared.LanguageKey, result);
            CollectRationale(content, relativePath, result);

            return result.Nodes.Count > 1 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<SymbolChunkRange> ToChunkRanges(GraphExtractionResult extraction)
    {
        return extraction.Nodes
            .Where(node => node.Kind is not "file" and not "import" and not "call")
            .Select(node => new SymbolChunkRange(
                $"ts_{node.Kind}",
                node.Label,
                node.ParentSymbol,
                node.StartLine,
                node.EndLine,
                node.Content))
            .Where(range => !string.IsNullOrWhiteSpace(range.Content))
            .ToList();
    }

    private static void CollectSymbols(Language language, Node root, string content, string relativePath, string languageKey, GraphExtractionResult result)
    {
        var queryText = TreeSitterQueries.SymbolsFor(languageKey);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return;
        }

        using var query = new Query(language, queryText);
        using var cursor = query.Execute(root);
        foreach (var match in cursor.Matches)
        {
            string? symbol = null;
            Node? unit = null;
            foreach (var capture in match.Captures)
            {
                if (capture.Name == "symbol")
                {
                    symbol = capture.Node.Text;
                }
                else if (capture.Name == "unit")
                {
                    unit = capture.Node;
                }
            }

            if (string.IsNullOrWhiteSpace(symbol) || unit is null)
            {
                continue;
            }

            var startLine = unit.StartPosition.Row + 1;
            var endLine = unit.EndPosition.Row + 1;
            var nodeId = $"sym:{relativePath}:{symbol}:{startLine}";
            var text = unit.Text.Trim();
            result.Nodes.Add(new ExtractionNode
            {
                Id = nodeId,
                Label = symbol,
                SourceFile = relativePath,
                SourceLocation = $"L{startLine}",
                Kind = InferKind(unit.Type),
                StartLine = startLine,
                EndLine = endLine,
                Content = text,
                ParentSymbol = InferParent(unit)
            });
            result.Edges.Add(new ExtractionEdge
            {
                Source = $"file:{relativePath}",
                Target = nodeId,
                Relation = "contains",
                Confidence = ExtractionConfidence.Extracted,
                ConfidenceScore = 1.0,
                SourceFile = relativePath
            });
        }
    }

    private static void CollectImports(Language language, Node root, string relativePath, string languageKey, GraphExtractionResult result, string fileNodeId)
    {
        var queryText = TreeSitterQueries.ImportsFor(languageKey);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return;
        }

        using var query = new Query(language, queryText);
        using var cursor = query.Execute(root);
        foreach (var capture in cursor.Captures)
        {
            if (capture.Name != "import")
            {
                continue;
            }

            var import = NormalizeImport(capture.Node.Text);
            if (string.IsNullOrWhiteSpace(import))
            {
                continue;
            }

            var importId = $"import:{relativePath}:{import}";
            result.Nodes.Add(new ExtractionNode
            {
                Id = importId,
                Label = import,
                SourceFile = relativePath,
                Kind = "import",
                StartLine = capture.Node.StartPosition.Row + 1,
                EndLine = capture.Node.EndPosition.Row + 1,
                Content = capture.Node.Text
            });
            result.Edges.Add(new ExtractionEdge
            {
                Source = fileNodeId,
                Target = importId,
                Relation = "imports",
                Confidence = ExtractionConfidence.Extracted,
                ConfidenceScore = 1.0,
                SourceFile = relativePath
            });
        }
    }

    private static void CollectCalls(Language language, Node root, string relativePath, string languageKey, GraphExtractionResult result)
    {
        var queryText = TreeSitterQueries.CallsFor(languageKey);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return;
        }

        var symbols = result.Nodes
            .Where(node => node.Kind is not "file" and not "import" and not "call")
            .GroupBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        using var query = new Query(language, queryText);
        using var cursor = query.Execute(root);
        foreach (var capture in cursor.Captures)
        {
            if (capture.Name != "call")
            {
                continue;
            }

            var call = capture.Node.Text.Trim();
            if (string.IsNullOrWhiteSpace(call))
            {
                continue;
            }

            var caller = FindEnclosingSymbol(result, capture.Node.StartPosition.Row + 1) ?? $"file:{relativePath}";
            var callId = $"call:{relativePath}:{call}:{capture.Node.StartPosition.Row + 1}";
            result.Nodes.Add(new ExtractionNode
            {
                Id = callId,
                Label = call,
                SourceFile = relativePath,
                Kind = "call",
                StartLine = capture.Node.StartPosition.Row + 1,
                EndLine = capture.Node.EndPosition.Row + 1,
                Content = capture.Node.Text
            });
            result.Edges.Add(new ExtractionEdge
            {
                Source = caller,
                Target = callId,
                Relation = "calls",
                Confidence = ExtractionConfidence.Extracted,
                ConfidenceScore = 1.0,
                SourceFile = relativePath
            });

            if (symbols.TryGetValue(call, out var target))
            {
                result.Edges.Add(new ExtractionEdge
                {
                    Source = caller,
                    Target = target,
                    Relation = "calls",
                    Confidence = ExtractionConfidence.Inferred,
                    ConfidenceScore = 0.85,
                    SourceFile = relativePath
                });
            }
        }
    }

    private static void CollectRationale(string content, string relativePath, GraphExtractionResult result)
    {
        var lines = content.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!TryParseRationale(lines[index], out var tag, out var text))
            {
                continue;
            }

            var lineNumber = index + 1;
            var rationaleId = $"rationale:{relativePath}:{lineNumber}";
            result.Nodes.Add(new ExtractionNode
            {
                Id = rationaleId,
                Label = $"{tag}: {text}",
                SourceFile = relativePath,
                SourceLocation = $"L{lineNumber}",
                Kind = "rationale",
                StartLine = lineNumber,
                EndLine = lineNumber,
                Content = lines[index].Trim()
            });

            var anchor = FindEnclosingSymbol(result, lineNumber) ?? $"file:{relativePath}";
            result.Edges.Add(new ExtractionEdge
            {
                Source = rationaleId,
                Target = anchor,
                Relation = "rationale_for",
                Confidence = ExtractionConfidence.Extracted,
                ConfidenceScore = 1.0,
                SourceFile = relativePath
            });
        }
    }

    private static bool TryParseRationale(string line, out string tag, out string text)
    {
        tag = string.Empty;
        text = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length < 8)
        {
            return false;
        }

        string? matchedTag = null;
        if (trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var body = trimmed[2..].TrimStart();
            matchedTag = MatchRationaleTag(body, out text);
        }
        else if (trimmed.StartsWith('#'))
        {
            var body = trimmed[1..].TrimStart();
            matchedTag = MatchRationaleTag(body, out text);
        }

        if (matchedTag is null)
        {
            return false;
        }

        tag = matchedTag;
        return !string.IsNullOrWhiteSpace(text);
    }

    private static string? MatchRationaleTag(string body, out string text)
    {
        text = string.Empty;
        foreach (var candidate in new[] { "NOTE", "IMPORTANT", "HACK", "WHY", "TODO", "FIXME" })
        {
            if (!body.StartsWith(candidate, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            text = body[candidate.Length..].TrimStart(':', ' ', '-');
            return candidate.ToUpperInvariant();
        }

        return null;
    }

    private static string? FindEnclosingSymbol(GraphExtractionResult result, int line)
    {
        return result.Nodes
            .Where(node => node.Kind is not "file" and not "import" and not "call")
            .Where(node => line >= node.StartLine && line <= node.EndLine)
            .OrderBy(node => node.EndLine - node.StartLine)
            .Select(node => node.Id)
            .FirstOrDefault();
    }

    private static string InferKind(string nodeType)
    {
        if (nodeType.Contains("class", StringComparison.OrdinalIgnoreCase)) return "class";
        if (nodeType.Contains("interface", StringComparison.OrdinalIgnoreCase)) return "interface";
        if (nodeType.Contains("method", StringComparison.OrdinalIgnoreCase) || nodeType.Contains("function", StringComparison.OrdinalIgnoreCase)) return "function";
        if (nodeType.Contains("property", StringComparison.OrdinalIgnoreCase)) return "property";
        return "symbol";
    }

    private static string InferParent(Node unit)
    {
        var parent = unit.Parent;
        while (parent is not null)
        {
            if (parent.Type.Contains("class", StringComparison.OrdinalIgnoreCase) ||
                parent.Type.Contains("struct", StringComparison.OrdinalIgnoreCase) ||
                parent.Type.Contains("interface", StringComparison.OrdinalIgnoreCase))
            {
                var nameChild = parent.NamedChildren.FirstOrDefault(child => child.Type is "identifier" or "type_identifier" or "property_identifier");
                if (nameChild is not null)
                {
                    return nameChild.Text;
                }
            }

            parent = parent.Parent;
        }

        return string.Empty;
    }

    private static string NormalizeImport(string raw) =>
        raw.Trim().Trim('\'', '"', ';', '(', ')');

    private static int CountLines(string content) =>
        string.IsNullOrEmpty(content) ? 1 : content.Count(c => c == '\n') + 1;
}
