namespace Autocorrect.Core.Brain;

public enum ExtractionConfidence
{
    Extracted,
    Inferred,
    Ambiguous
}

public sealed class ExtractionNode
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public string SourceLocation { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ParentSymbol { get; set; } = string.Empty;
}

public sealed class ExtractionEdge
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
    public ExtractionConfidence Confidence { get; set; } = ExtractionConfidence.Extracted;
    public double ConfidenceScore { get; set; } = 1.0;
    public string SourceFile { get; set; } = string.Empty;
}

public sealed class GraphExtractionResult
{
    public string SourceFile { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public List<ExtractionNode> Nodes { get; set; } = new();
    public List<ExtractionEdge> Edges { get; set; } = new();
}

public sealed class PromptSymbolParseResult
{
    public string OriginalPrompt { get; set; } = string.Empty;
    public List<string> Symbols { get; set; } = new();
    public List<string> FilePaths { get; set; } = new();
    public List<string> TypeNames { get; set; } = new();
    public List<string> MethodNames { get; set; } = new();
}
