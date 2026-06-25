namespace Autocorrect.Cli;

internal static class WoodyConsole
{
    private const int Width = 70;

    public static bool UseColor { get; } = !Console.IsOutputRedirected;

    public static void WriteBrandBanner()
    {
        Console.WriteLine();
        WriteLine("  __        __        _ ", Brand);
        WriteLine(@"  \ \      / /__   __| | ___   ___ ", Brand);
        WriteLine(@"   \ \ /\ / / _ \ / _` |/ _ \ / _ \", Brand);
        WriteLine(@"    \ V  V / (_) | (_| | (_) | (_) |", Brand);
        WriteLine(@"     \_/\_/ \___/ \__,_|\___/ \___/", Brand);
        WriteLine("          token-smart project brain", Muted);
        WriteLine(new string('─', Width), Muted);
        Console.WriteLine();
    }

    public static void WriteCommandHeader(string command, string subtitle)
    {
        WriteLine($"  {command.ToUpperInvariant()}", Accent);
        WriteLine($"  {subtitle}", Muted);
        Console.WriteLine();
    }

    public static void WriteHelp()
    {
        WriteBrandBanner();
        WriteLine("  Usage: woody <command> [options] [text]", Foreground);
        Console.WriteLine();

        foreach (var (name, summary) in PrimaryCommands)
        {
            Write("  ", Foreground);
            Write(name.PadRight(10), Accent);
            WriteLine(summary, Foreground);
        }

        Console.WriteLine();
        WriteLine("  Options", Accent);
        WriteLabel("  --path", "Project folder (default: active project in Woody)");
        WriteLabel("  --rag", "Vector search only");
        WriteLabel("  --ast", "AST graph search only");
        WriteLabel("  --force", "Full re-index (with reload)");
        Console.WriteLine();
        WriteLine("  Examples", Accent);
        WriteDim("  woody index");
        WriteDim("  woody brain");
        WriteDim("  woody prompt \"where is bos configured\"");
        WriteDim("  woody search \"login validation\" --ast");
        WriteDim("  woody reload");
        WriteDim("  woody reload --force");
        WriteDim("  woody doctor");
        Console.WriteLine();
    }

    public static void WriteLabel(string label, string value)
    {
        Write(label.PadRight(16), Label);
        WriteLine(value, Foreground);
    }

    public static void WriteMeta(string label, string value) => WriteLabel($"  {label}", value);

    public static void WriteDivider(string? title = null)
    {
        Console.WriteLine();
        if (string.IsNullOrWhiteSpace(title))
        {
            WriteLine(new string('─', Width), Muted);
            return;
        }

        WriteLine($"  {title}", Accent);
        WriteLine(new string('─', Width), Muted);
        Console.WriteLine();
    }

    public static void WriteBlock(string text, ConsoleColor color = ConsoleColor.Gray)
    {
        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
        {
            WriteLine($"  {line}", color);
        }
    }

    public static void WriteSuccess(string text) => WriteLine($"  ✓ {text}", Success);

    public static void WriteWarn(string text) => WriteLine($"  ! {text}", Warning);

    public static void WriteError(string text) => WriteLine($"  ✗ {text}", Error);

    public static void WriteDim(string text) => WriteLine($"  {text}", Muted);

    public static void WriteRankedHit(int rank, RetrievalHitView hit)
    {
        var line = hit.StartLine > 0 && hit.EndLine > 0
            ? $"{hit.FilePath}:{hit.StartLine}-{hit.EndLine}"
            : hit.FilePath;
        Write($"  {rank,2}. ", Muted);
        Write(line, Accent);
        if (!string.IsNullOrWhiteSpace(hit.Symbol))
        {
            Write($"  ({hit.Symbol})", Foreground);
        }

        WriteLine($"  [{hit.Score:0.00}]", Muted);
        if (!string.IsNullOrWhiteSpace(hit.Reason))
        {
            WriteDim($"     {hit.Reason}");
        }

        if (!string.IsNullOrWhiteSpace(hit.Preview))
        {
            WriteDim($"     {TrimPreview(hit.Preview)}");
        }
    }

    private static string TrimPreview(string preview) =>
        preview.Length <= 96 ? preview : preview[..93] + "...";

    public static readonly (string Name, string Summary)[] PrimaryCommands =
    [
        ("index", "Build the project brain (scan, AST, vectors)"),
        ("brain", "Open AST + vector brain in the browser"),
        ("prompt", "Compile a short agent prompt with file:line targets"),
        ("search", "Search the brain — files, symbols, and line regions"),
        ("reload", "Sync and reload when project files changed"),
        ("doctor", "Health check embedder, vectors, Ollama, and index")
    ];

    private static readonly ConsoleColor Brand = ConsoleColor.Green;
    private static readonly ConsoleColor Accent = ConsoleColor.Cyan;
    private static readonly ConsoleColor Label = ConsoleColor.DarkCyan;
    private static readonly ConsoleColor Muted = ConsoleColor.DarkGray;
    private static readonly ConsoleColor Foreground = ConsoleColor.Gray;
    private static readonly ConsoleColor Success = ConsoleColor.Green;
    private static readonly ConsoleColor Warning = ConsoleColor.Yellow;
    private static readonly ConsoleColor Error = ConsoleColor.Red;

    private static void Write(string text, ConsoleColor color)
    {
        if (!UseColor)
        {
            Console.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    private static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }
}

internal sealed record RetrievalHitView(
    string FilePath,
    string Symbol,
    double Score,
    string Reason,
    string Preview,
    int StartLine,
    int EndLine);
