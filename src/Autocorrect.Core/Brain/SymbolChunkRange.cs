namespace Autocorrect.Core.Brain;

public sealed record SymbolChunkRange(
    string Kind,
    string Symbol,
    string ParentSymbol,
    int StartLine,
    int EndLine,
    string Content);
