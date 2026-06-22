using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Autocorrect.Core.Brain;

// Roslyn-based C# symbol chunking for precise method/class boundaries.
public static class CsharpAstChunker
{
    public static IReadOnlyList<SymbolChunkRange> Extract(string content, string filePath)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<SymbolChunkRange>();
        }

        var tree = CSharpSyntaxTree.ParseText(content, path: filePath);
        var root = tree.GetCompilationUnitRoot();
        var ranges = new List<SymbolChunkRange>();
        foreach (var member in root.Members)
        {
            WalkMember(member, string.Empty, ranges);
        }

        return ranges.Count > 0 ? ranges : Array.Empty<SymbolChunkRange>();
    }

    private static void WalkMember(MemberDeclarationSyntax member, string parent, List<SymbolChunkRange> ranges)
    {
        if (member is BaseNamespaceDeclarationSyntax namespaceDecl)
        {
            foreach (var nested in namespaceDecl.Members)
            {
                WalkMember(nested, parent, ranges);
            }

            return;
        }

        CollectMember(member, parent, ranges);
    }

    private static void CollectMember(MemberDeclarationSyntax member, string parent, List<SymbolChunkRange> ranges)
    {
        var symbol = member switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier.Text,
            MethodDeclarationSyntax method => method.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return;
        }

        var span = member.FullSpan;
        var startLine = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var endLine = member.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        var text = member.ToFullString().Trim();
        if (text.Length == 0)
        {
            return;
        }

        var kind = member switch
        {
            ClassDeclarationSyntax => "csharp_class",
            InterfaceDeclarationSyntax => "csharp_interface",
            RecordDeclarationSyntax => "csharp_record",
            StructDeclarationSyntax => "csharp_struct",
            EnumDeclarationSyntax => "csharp_enum",
            MethodDeclarationSyntax => "csharp_method",
            PropertyDeclarationSyntax => "csharp_property",
            _ => "csharp_symbol"
        };

        ranges.Add(new SymbolChunkRange(kind, symbol, parent, startLine, endLine, text));

        if (member is TypeDeclarationSyntax typeDecl)
        {
            foreach (var nested in typeDecl.Members)
            {
                WalkMember(nested, symbol, ranges);
            }
        }
    }
}
