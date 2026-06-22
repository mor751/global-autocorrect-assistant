using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Autocorrect.Core.Brain;

public sealed class ProjectSourceFile
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public DateTimeOffset ModifiedAt { get; set; }
    public long SizeBytes { get; set; }
}

public interface IProjectScanner
{
    IReadOnlyList<ProjectSourceFile> Scan(string projectRoot, IndexOptions options, out IReadOnlyList<SkippedFile> skipped);
}

public interface IProjectAnalyzer
{
    ProjectBrainData Analyze(string projectRoot, IReadOnlyList<ProjectSourceFile> files, IndexOptions options);
}

public interface IProjectChunker
{
    IReadOnlyList<ProjectChunk> Chunk(ProjectSourceFile source, ProjectFileSummary summary, int maxChunks);
}

public sealed class ProjectScanner : IProjectScanner
{
    // Binary or private file types that can never become useful text context.
    private static readonly HashSet<string> BinaryOrPrivateExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico", ".icns", ".mp4", ".mov", ".avi", ".mkv",
        ".mp3", ".wav", ".flac", ".ogg", ".woff", ".woff2", ".ttf", ".otf", ".eot", ".pdf", ".zip", ".rar",
        ".7z", ".gz", ".tar", ".exe", ".dll", ".pdb", ".so", ".dylib", ".bin", ".dat", ".class", ".o",
        ".pem", ".key", ".pfx", ".crt", ".cer", ".p12", ".keystore"
    };

    // Text files that are noise for prompt context (lock files, source maps, logs, data dumps).
    private static readonly HashSet<string> NoiseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".map", ".lock", ".log", ".tmp", ".temp", ".bak", ".swp", ".snap", ".tsbuildinfo",
        ".svg", ".csv", ".tsv", ".min"
    };

    private static readonly HashSet<string> NoiseFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "bun.lockb", "npm-shrinkwrap.json",
        "composer.lock", "poetry.lock", "cargo.lock", "gemfile.lock", ".ds_store", "thumbs.db"
    };

    // Denylist scan: take every text file in the folder except secrets, binaries, noise, oversized, or ignored folders.
    public IReadOnlyList<ProjectSourceFile> Scan(string projectRoot, IndexOptions options, out IReadOnlyList<SkippedFile> skipped)
    {
        var root = Path.GetFullPath(projectRoot);
        var projectId = BrainStorage.ProjectKey(root);
        var files = new List<ProjectSourceFile>();
        var skippedFiles = new List<SkippedFile>();

        foreach (var path in EnumerateCandidateFiles(root, options))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var fileName = Path.GetFileName(path);
            var extension = Path.GetExtension(path);

            if (files.Count >= options.MaxFiles)
            {
                skippedFiles.Add(new SkippedFile { Path = relative, Reason = $"file limit reached ({options.MaxFiles})" });
                continue;
            }

            var staticReason = StaticSkipReason(fileName, extension);
            if (staticReason is not null)
            {
                skippedFiles.Add(new SkippedFile { Path = relative, Reason = staticReason });
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (Exception ex)
            {
                skippedFiles.Add(new SkippedFile { Path = relative, Reason = $"read error: {ex.GetType().Name}" });
                continue;
            }

            if (info.Length == 0)
            {
                skippedFiles.Add(new SkippedFile { Path = relative, Reason = "empty file" });
                continue;
            }

            if (info.Length > options.MaxFileSizeBytes)
            {
                skippedFiles.Add(new SkippedFile { Path = relative, Reason = $"too large ({info.Length / 1024} KB > {options.MaxFileSizeBytes / 1024} KB)" });
                continue;
            }

            string content;
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (LooksBinary(bytes))
                {
                    skippedFiles.Add(new SkippedFile { Path = relative, Reason = "binary content" });
                    continue;
                }

                content = SecretScanner.Redact(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception ex)
            {
                skippedFiles.Add(new SkippedFile { Path = relative, Reason = $"read error: {ex.GetType().Name}" });
                continue;
            }

            files.Add(new ProjectSourceFile
            {
                ProjectId = projectId,
                ProjectRoot = root,
                AbsolutePath = path,
                RelativePath = relative,
                Extension = extension,
                Content = content,
                Hash = Hash(content),
                ModifiedAt = info.LastWriteTimeUtc,
                SizeBytes = info.Length
            });
        }

        skipped = skippedFiles;
        return Prioritize(files);
    }

    private static string? StaticSkipReason(string fileName, string extension)
    {
        if (SecretScanner.LooksLikeSecretFile(fileName)) return "secret/private file";
        if (BinaryOrPrivateExtensions.Contains(extension)) return $"binary type ({extension})";
        if (NoiseFileNames.Contains(fileName)) return "lock/metadata file";
        if (NoiseExtensions.Contains(extension)) return $"noise type ({extension})";
        if (LooksGeneratedOrMinified(fileName)) return "generated or minified";
        return null;
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root, IndexOptions options)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
            }
            catch
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry))
                {
                    if ((!name.StartsWith('.') || name.Equals(".cursor", StringComparison.OrdinalIgnoreCase)) &&
                        !options.IgnoredFolders.Contains(name))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                yield return entry;
            }
        }
    }

    private static List<ProjectSourceFile> Prioritize(IEnumerable<ProjectSourceFile> files)
    {
        return files
            .OrderByDescending(f => Priority(f.RelativePath))
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int Priority(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("readme") || lower.Contains("docs/")) return 100;
        if (lower is "package.json" or "tsconfig.json" || lower.Contains("config")) return 90;
        if (lower.Contains("src/app") || lower.Contains("pages/") || lower.Contains("routes/")) return 80;
        if (lower.Contains("components/")) return 70;
        if (lower.Contains("lib/") || lower.Contains("services/") || lower.Contains("api/")) return 60;
        if (lower.Contains("schema") || lower.Contains("migration") || lower.Contains("prisma")) return 50;
        if (lower.Contains("test") || lower.Contains("spec")) return 30;
        return 10;
    }

    private static bool LooksGeneratedOrMinified(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        return lower.EndsWith(".min.js", StringComparison.Ordinal) ||
               lower.EndsWith(".min.css", StringComparison.Ordinal) ||
               lower.Contains(".generated.", StringComparison.Ordinal) ||
               lower.Contains(".g.", StringComparison.Ordinal);
    }

    private static bool LooksBinary(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length, 4096);
        for (var i = 0; i < sample; i++)
        {
            if (bytes[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public sealed class ProjectAnalyzer : IProjectAnalyzer
{
    public ProjectBrainData Analyze(string projectRoot, IReadOnlyList<ProjectSourceFile> files, IndexOptions options)
    {
        var root = Path.GetFullPath(projectRoot);
        var projectId = BrainStorage.ProjectKey(root);
        var brain = new ProjectBrainData
        {
            ProjectRoot = root,
            ProjectName = new DirectoryInfo(root).Name,
            Stack = StackDetector.Detect(root),
            IndexedAt = DateTimeOffset.Now
        };

        var projectNode = brain.Graph.AddNode("project", NodeType.Project, brain.ProjectName);
        foreach (var source in files)
        {
            var summary = BuildSummary(projectId, source);
            brain.Files.Add(summary);
            AddToGraph(brain.Graph, projectNode, summary);
            AddToFolders(brain.Folders, summary);
        }

        WireImportEdges(brain);
        brain.Rules = RuleExtractor.Extract(root, brain);
        return brain;
    }

    private static ProjectFileSummary BuildSummary(string projectId, ProjectSourceFile source)
    {
        var role = FileRoleDetector.Detect(source.RelativePath, source.Extension, source.Content);
        var (imports, exports, symbols) = CodeInspector.Inspect(source.Content);
        return new ProjectFileSummary
        {
            Id = $"{projectId}:{source.RelativePath}",
            ProjectId = projectId,
            Path = source.RelativePath,
            AbsolutePath = source.AbsolutePath,
            Extension = source.Extension,
            Language = LanguageFor(source.Extension, source.RelativePath),
            Role = role,
            DetectedRole = role.ToString().ToLowerInvariant(),
            Importance = Importance(source.RelativePath, role),
            Hash = source.Hash,
            Summary = CodeInspector.Summarize(role, symbols, source.Content),
            Imports = imports,
            Exports = exports,
            Symbols = symbols,
            LastModified = source.ModifiedAt,
            SizeBytes = source.SizeBytes,
            PreviewChunks = CodeInspector.Chunk(source.Content).ToList()
        };
    }

    private static string LanguageFor(string extension, string path) => extension.ToLowerInvariant() switch
    {
        ".ts" or ".tsx" => "typescript",
        ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
        ".cs" => "csharp",
        ".csproj" or ".sln" or ".props" or ".targets" or ".resx" or ".config" or ".xaml" => "xml",
        ".py" => "python",
        ".json" => "json",
        ".md" or ".mdx" => "markdown",
        ".css" or ".scss" or ".sass" or ".less" => "css",
        ".sql" => "sql",
        ".yaml" or ".yml" => "yaml",
        ".toml" => "toml",
        ".html" or ".htm" => "html",
        ".ps1" or ".psm1" or ".psd1" => "powershell",
        ".editorconfig" or ".ini" => "ini",
        _ when Path.GetFileName(path).Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) => "dockerfile",
        _ => "text"
    };

    private static double Importance(string path, FileRole role)
    {
        var lower = path.ToLowerInvariant();
        var score = role switch
        {
            FileRole.Docs => 0.95,
            FileRole.Config => 0.9,
            FileRole.Route or FileRole.Api => 0.86,
            FileRole.Component => 0.78,
            FileRole.Database => 0.74,
            FileRole.Util or FileRole.Hook => 0.68,
            FileRole.Test => 0.35,
            _ => 0.5
        };

        if (lower.Contains("auth") || lower.Contains("login")) score += 0.12;
        if (lower.Contains("readme") || lower.Contains("package.json")) score += 0.1;
        return Math.Clamp(score, 0, 1);
    }

    private static void AddToGraph(ProjectGraph graph, string projectNode, ProjectFileSummary file)
    {
        var fileId = $"file:{file.Path}";
        graph.AddNode(fileId, FileRoleDetector.ToNodeType(file.Role), Path.GetFileName(file.Path), file.Path);
        graph.AddEdge(projectNode, fileId, EdgeType.Contains);
        foreach (var symbol in file.Symbols.Take(6))
        {
            var symbolType = file.Role == FileRole.Hook ? NodeType.Hook : NodeType.Function;
            var symbolId = $"sym:{file.Path}:{symbol}";
            graph.AddNode(symbolId, symbolType, symbol, file.Path);
            graph.AddEdge(fileId, symbolId, EdgeType.Exports);
        }
    }

    private static void AddToFolders(ProjectFolders folders, ProjectFileSummary file)
    {
        switch (file.Role)
        {
            case FileRole.Route: folders.Routes.Add(file.Path); break;
            case FileRole.Component: folders.Components.Add(file.Path); break;
            case FileRole.Hook:
            case FileRole.Util: folders.Utils.Add(file.Path); break;
            case FileRole.Style: folders.Styles.Add(file.Path); break;
            case FileRole.Api: folders.Api.Add(file.Path); break;
            case FileRole.Database: folders.Data.Add(file.Path); break;
        }
    }

    private static void WireImportEdges(ProjectBrainData brain)
    {
        var byStem = brain.Files
            .GroupBy(f => StripExtension(f.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in brain.Files)
        {
            foreach (var import in file.Imports.Where(i => i.StartsWith('.')))
            {
                var resolved = StripExtension(NormalizeRelative(file.Path, import));
                if (byStem.TryGetValue(resolved, out var target) && target.Path != file.Path)
                {
                    brain.Graph.AddEdge($"file:{file.Path}", $"file:{target.Path}", EdgeType.Imports);
                }
            }
        }
    }

    private static string NormalizeRelative(string fromPath, string import)
    {
        var baseDir = Path.GetDirectoryName(fromPath)?.Replace('\\', '/') ?? string.Empty;
        var combined = Path.Combine(baseDir, import).Replace('\\', '/');
        var segments = new Stack<string>();
        foreach (var segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == ".." && segments.Count > 0) segments.Pop();
            else if (segment != "..") segments.Push(segment);
        }

        return string.Join('/', segments.Reverse());
    }

    private static string StripExtension(string path)
    {
        var withoutExtension = path.Contains('.') ? path[..path.LastIndexOf('.')] : path;
        return withoutExtension.EndsWith("/index", StringComparison.OrdinalIgnoreCase)
            ? withoutExtension[..^"/index".Length]
            : withoutExtension;
    }
}

public sealed partial class ProjectChunker : IProjectChunker
{
    private readonly TreeSitterExtractor _treeSitter = new();

    public IReadOnlyList<ProjectChunk> Chunk(ProjectSourceFile source, ProjectFileSummary summary, int maxChunks)
    {
        var treeRanges = TryTreeSitterRanges(source);
        if (treeRanges.Count > 0)
        {
            return treeRanges.Take(maxChunks).Select((range, index) => BuildChunk(source, summary, range, index)).ToList();
        }

        var ranges = summary.Language switch
        {
            "markdown" => MarkdownRanges(source.Content),
            "typescript" or "javascript" => SymbolRanges(source.Content, JsSymbolRegex(), "code_symbol"),
            "python" => SymbolRanges(source.Content, PythonSymbolRegex(), "python_symbol"),
            "csharp" => CsharpRanges(source.Content, source.RelativePath),
            "sql" => SymbolRanges(source.Content, SqlSymbolRegex(), "sql_block"),
            "json" => JsonRanges(source.Content),
            "css" => SymbolRanges(source.Content, CssSelectorRegex(), "style_block"),
            _ => new List<ChunkRange>()
        };

        if (ranges.Count == 0)
        {
            ranges = FixedRanges(source.Content, 2400, 260, "text");
        }

        return ranges.Take(maxChunks).Select((range, index) => BuildChunk(source, summary, range, index)).ToList();
    }

    private List<ChunkRange> TryTreeSitterRanges(ProjectSourceFile source)
    {
        var extraction = _treeSitter.TryExtract(source.RelativePath, source.Content);
        if (extraction is not null)
        {
            return _treeSitter.ToChunkRanges(extraction)
                .Select(range => new ChunkRange(range.Kind, range.Symbol, range.StartLine, range.EndLine, range.Content, range.ParentSymbol))
                .ToList();
        }

        if (source.Extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var ast = CsharpAstChunker.Extract(source.Content, source.RelativePath);
            if (ast.Count > 0)
            {
                return ast.Select(range => new ChunkRange(range.Kind, range.Symbol, range.StartLine, range.EndLine, range.Content, range.ParentSymbol)).ToList();
            }
        }

        return new List<ChunkRange>();
    }

    private static ProjectChunk BuildChunk(ProjectSourceFile source, ProjectFileSummary summary, ChunkRange range, int index)
    {
        var content = range.Content.Trim();
        var hash = ProjectScanner.Hash($"{source.Hash}:{range.StartLine}:{range.EndLine}:{content}");
        var pathFolder = Path.GetDirectoryName(source.RelativePath)?.Replace('\\', '/') ?? string.Empty;
        return new ProjectChunk
        {
            Id = StablePointId($"{source.ProjectId}:{source.RelativePath}:{hash[..16]}"),
            ProjectId = source.ProjectId,
            ProjectRoot = source.ProjectRoot,
            FilePath = source.RelativePath,
            FileName = Path.GetFileName(source.RelativePath),
            Folder = pathFolder,
            Extension = source.Extension,
            Language = summary.Language,
            ChunkType = range.Kind,
            Symbol = range.Symbol,
            ParentSymbol = range.ParentSymbol,
            StartLine = range.StartLine,
            EndLine = range.EndLine,
            Content = content,
            ContentPreview = Preview(content),
            MetadataJson = JsonSerializer.Serialize(new { summary.Role, summary.DetectedRole, Index = index }, BrainJson.Options),
            Imports = summary.Imports,
            Exports = summary.Exports,
            Tags = TagsFor(summary, range),
            FileHash = source.Hash,
            ChunkHash = hash,
            Importance = summary.Importance,
            IndexedAt = DateTimeOffset.UtcNow
        };
    }

    private static List<string> TagsFor(ProjectFileSummary summary, ChunkRange range)
    {
        var tags = new List<string> { summary.Role.ToString().ToLowerInvariant(), summary.Language };
        if (!string.IsNullOrWhiteSpace(range.Symbol)) tags.Add(range.Symbol);
        return tags.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList();
    }

    private static List<ChunkRange> CsharpRanges(string content, string relativePath)
    {
        var ast = CsharpAstChunker.Extract(content, relativePath);
        if (ast.Count > 0)
        {
            return ast.Select(range => new ChunkRange(range.Kind, range.Symbol, range.StartLine, range.EndLine, range.Content, range.ParentSymbol)).ToList();
        }

        return SymbolRanges(content, CsharpSymbolRegex(), "csharp_symbol");
    }

    private static List<ChunkRange> MarkdownRanges(string content)
    {
        var lines = Lines(content);
        var starts = lines.Select((line, index) => new { line, index })
            .Where(x => x.line.StartsWith('#'))
            .Select(x => x.index)
            .ToList();
        if (starts.Count == 0) return FixedRanges(content, 2600, 200, "markdown");

        var ranges = new List<ChunkRange>();
        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1] - 1 : lines.Count - 1;
            var text = string.Join('\n', lines.Skip(start).Take(end - start + 1));
            if (text.Length > 3400)
            {
                ranges.AddRange(FixedRanges(text, 2800, 180, "markdown_section", start + 1));
            }
            else
            {
                ranges.Add(new ChunkRange("markdown_section", CleanSymbol(lines[start].TrimStart('#', ' ')), start + 1, end + 1, text));
            }
        }

        return ranges;
    }

    private static List<ChunkRange> JsonRanges(string content)
    {
        return content.Length < 3500
            ? new List<ChunkRange> { new("json", string.Empty, 1, Lines(content).Count, content) }
            : FixedRanges(content, 2200, 200, "json");
    }

    private static List<ChunkRange> SymbolRanges(string content, Regex regex, string kind)
    {
        var matches = regex.Matches(content).Cast<Match>().Where(m => m.Success).ToList();
        if (matches.Count == 0) return new List<ChunkRange>();

        var lineStarts = BuildLineStarts(content);
        var ranges = new List<ChunkRange>();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var text = content[start..end].Trim();
            if (text.Length == 0) continue;
            var startLine = LineOf(lineStarts, start);
            var endLine = LineOf(lineStarts, Math.Max(start, end - 1));
            var symbol = matches[i].Groups.Count > 1 ? CleanSymbol(matches[i].Groups[1].Value) : string.Empty;
            if (text.Length > 4200)
            {
                ranges.AddRange(FixedRanges(text, 2800, 260, kind, startLine));
            }
            else
            {
                ranges.Add(new ChunkRange(kind, symbol, startLine, endLine, text));
            }
        }

        return ranges;
    }

    private static List<ChunkRange> FixedRanges(string content, int chunkSize, int overlap, string kind, int baseLine = 1)
    {
        var normalized = content.Replace("\r\n", "\n").Trim();
        var ranges = new List<ChunkRange>();
        if (normalized.Length == 0) return ranges;

        for (var start = 0; start < normalized.Length; start += Math.Max(1, chunkSize - overlap))
        {
            var text = normalized.Substring(start, Math.Min(chunkSize, normalized.Length - start));
            var startLine = baseLine + CountNewLines(normalized[..start]);
            var endLine = startLine + CountNewLines(text);
            ranges.Add(new ChunkRange(kind, string.Empty, startLine, endLine, text));
            if (start + chunkSize >= normalized.Length) break;
        }

        return ranges;
    }

    private static List<string> Lines(string content) => content.Replace("\r\n", "\n").Split('\n').ToList();

    private static List<int> BuildLineStarts(string content)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n') starts.Add(i + 1);
        }

        return starts;
    }

    private static int LineOf(List<int> lineStarts, int index)
    {
        var line = lineStarts.BinarySearch(index);
        if (line >= 0) return line + 1;
        return Math.Max(1, ~line);
    }

    private static int CountNewLines(string text) => text.Count(c => c == '\n');

    private static string Preview(string text)
    {
        var collapsed = Regex.Replace(text, @"\s+", " ").Trim();
        return collapsed.Length <= 260 ? collapsed : collapsed[..260] + "...";
    }

    private static string CleanSymbol(string value)
    {
        return value.Trim().Trim('{', '(', ':', ';').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    private static string StablePointId(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    private sealed record ChunkRange(string Kind, string Symbol, int StartLine, int EndLine, string Content, string ParentSymbol = "");

    [GeneratedRegex(@"(?m)^\s*(?:export\s+)?(?:async\s+)?(?:function|class|interface|type|enum)\s+([A-Za-z_][A-Za-z0-9_]*)|^\s*(?:export\s+)?const\s+([A-Za-z_][A-Za-z0-9_]*)\s*=", RegexOptions.Compiled)]
    private static partial Regex JsSymbolRegex();

    [GeneratedRegex(@"(?m)^\s*(?:class|def)\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled)]
    private static partial Regex PythonSymbolRegex();

    [GeneratedRegex(@"(?m)^\s*(?:public|private|internal|protected|static|sealed|partial|abstract|\s)*\s*(?:class|interface|record|struct|enum)\s+([A-Za-z_][A-Za-z0-9_]*)|^\s*(?:public|private|internal|protected|static|async|virtual|override|\s)+[A-Za-z0-9_<>,\[\]?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled)]
    private static partial Regex CsharpSymbolRegex();

    [GeneratedRegex(@"(?im)^\s*(CREATE\s+(?:TABLE|VIEW|PROCEDURE|FUNCTION)|ALTER\s+TABLE)\s+([A-Za-z0-9_.""\[\]]+)", RegexOptions.Compiled)]
    private static partial Regex SqlSymbolRegex();

    [GeneratedRegex(@"(?m)^\s*([.#]?[A-Za-z_][A-Za-z0-9_.:#\-\s>]+)\s*\{", RegexOptions.Compiled)]
    private static partial Regex CssSelectorRegex();
}
