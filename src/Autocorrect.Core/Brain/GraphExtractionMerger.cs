namespace Autocorrect.Core.Brain;

// Merges tree-sitter extraction dicts into the project graph (graphify build_graph equivalent).
public static class GraphExtractionMerger
{
    public static void Apply(ProjectBrainData brain, IReadOnlyList<GraphExtractionResult> extractions)
    {
        foreach (var extraction in extractions)
        {
            foreach (var node in extraction.Nodes)
            {
                var nodeType = node.Kind switch
                {
                    "file" => NodeType.File,
                    "class" or "interface" or "struct" => NodeType.Component,
                    "function" or "method" or "property" => NodeType.Function,
                    "import" => NodeType.Config,
                    "rationale" => NodeType.Config,
                    _ => NodeType.Function
                };

                brain.Graph.UpsertNode(node.Id, nodeType, node.Label, node.SourceFile, BuildMeta(node));
            }

            foreach (var edge in extraction.Edges)
            {
                var edgeType = edge.Relation switch
                {
                    "imports" => EdgeType.Imports,
                    "calls" => EdgeType.Calls,
                    "contains" => EdgeType.Contains,
                    "rationale_for" => EdgeType.RelatedTo,
                    _ => EdgeType.RelatedTo
                };

                brain.Graph.AddEdge(edge.Source, edge.Target, edgeType);
            }

            var summary = brain.Files.FirstOrDefault(file => file.Path.Equals(extraction.SourceFile, StringComparison.OrdinalIgnoreCase));
            if (summary is null)
            {
                continue;
            }

            var imports = extraction.Edges
                .Where(edge => edge.Relation == "imports")
                .Select(edge => extraction.Nodes.FirstOrDefault(node => node.Id == edge.Target)?.Label)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var symbols = extraction.Nodes
                .Where(node => node.Kind is not "file" and not "import" and not "call")
                .Select(node => node.Label)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (imports.Count > 0)
            {
                summary.Imports = imports;
            }

            if (symbols.Count > 0)
            {
                summary.Symbols = symbols;
                summary.Exports = symbols.Take(40).ToList();
            }
        }
    }

    private static Dictionary<string, string> BuildMeta(ExtractionNode node)
    {
        var meta = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kind"] = node.Kind,
            ["confidence"] = ExtractionConfidence.Extracted.ToString()
        };

        if (node.StartLine > 0)
        {
            meta["startLine"] = node.StartLine.ToString();
        }

        if (node.EndLine > 0)
        {
            meta["endLine"] = node.EndLine.ToString();
        }

        if (!string.IsNullOrWhiteSpace(node.ParentSymbol))
        {
            meta["parentSymbol"] = node.ParentSymbol;
        }

        if (!string.IsNullOrWhiteSpace(node.SourceLocation))
        {
            meta["sourceLocation"] = node.SourceLocation;
        }

        return meta;
    }
}
