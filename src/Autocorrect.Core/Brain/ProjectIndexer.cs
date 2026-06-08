using System.IO;

namespace Autocorrect.Core.Brain;

public sealed class IndexOptions
{
    public int MaxFileSizeBytes { get; set; } = 200 * 1024;
    public int MaxFiles { get; set; } = 1500;
    public HashSet<string> IgnoredFolders { get; set; } = DefaultIgnoredFolders();

    public static HashSet<string> DefaultIgnoredFolders() => new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "build", ".next", "out", "coverage",
        ".turbo", ".cache", ".vercel", "bin", "obj", ".vs", ".idea", "vendor", "__pycache__"
    };
}

public interface IProjectIndexer
{
    ProjectBrainData Index(string projectRoot, IndexOptions options);
}

// Scans a project folder, summarizes useful files, detects the stack/rules, and builds the neural graph.
public sealed class ProjectIndexer : IProjectIndexer
{
    private static readonly HashSet<string> UsefulExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".vue", ".svelte",
        ".cs", ".py", ".go", ".rb", ".rs", ".java",
        ".css", ".scss", ".sass", ".less",
        ".json", ".md", ".mdx", ".txt", ".yaml", ".yml", ".html", ".prisma", ".sql"
    };

    private static readonly HashSet<string> AllowedRootConfigs = new(StringComparer.OrdinalIgnoreCase)
    {
        "package.json", "readme.md", "tsconfig.json", "components.json", ".cursorrules", "agents.md", "claude.md"
    };

    public ProjectBrainData Index(string projectRoot, IndexOptions options)
    {
        var root = Path.GetFullPath(projectRoot);
        var brain = new ProjectBrainData
        {
            ProjectRoot = root,
            ProjectName = new DirectoryInfo(root).Name,
            Stack = StackDetector.Detect(root),
            IndexedAt = DateTimeOffset.Now
        };

        var projectNode = brain.Graph.AddNode("project", NodeType.Project, brain.ProjectName);

        foreach (var absolutePath in EnumerateCandidateFiles(root, options))
        {
            var summary = BuildSummary(root, absolutePath, options);
            if (summary is null)
            {
                continue;
            }

            brain.Files.Add(summary);
            AddToGraph(brain.Graph, projectNode, summary);
            AddToFolders(brain.Folders, summary);

            if (brain.Files.Count >= options.MaxFiles)
            {
                break;
            }
        }

        WireImportEdges(brain);
        brain.Rules = RuleExtractor.Extract(root, brain);
        return brain;
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
                    if (!name.StartsWith('.') && !options.IgnoredFolders.Contains(name) || IsAllowedDotFolder(name))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                yield return entry;
            }
        }
    }

    private static bool IsAllowedDotFolder(string name) => name is ".cursor";

    private ProjectFileSummary? BuildSummary(string root, string absolutePath, IndexOptions options)
    {
        var fileName = Path.GetFileName(absolutePath);
        var extension = Path.GetExtension(absolutePath);
        var relative = Path.GetRelativePath(root, absolutePath).Replace('\\', '/');

        if (SecretScanner.LooksLikeSecretFile(fileName))
        {
            return null;
        }

        var isRootConfig = !relative.Contains('/') && AllowedRootConfigs.Contains(fileName);
        if (!isRootConfig && !UsefulExtensions.Contains(extension))
        {
            return null;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(absolutePath);
            if (info.Length == 0 || info.Length > options.MaxFileSizeBytes)
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        string content;
        try
        {
            content = SecretScanner.Redact(File.ReadAllText(absolutePath));
        }
        catch
        {
            return null;
        }

        var role = FileRoleDetector.Detect(relative, extension, content);
        var (imports, exports, symbols) = CodeInspector.Inspect(content);

        return new ProjectFileSummary
        {
            Path = relative,
            AbsolutePath = absolutePath,
            Extension = extension,
            Role = role,
            Summary = CodeInspector.Summarize(role, symbols, content),
            Imports = imports,
            Exports = exports,
            Symbols = symbols,
            LastModified = info.LastWriteTime,
            SizeBytes = info.Length,
            PreviewChunks = CodeInspector.Chunk(content).ToList()
        };
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
            case FileRole.Route:
                folders.Routes.Add(file.Path);
                break;
            case FileRole.Component:
                folders.Components.Add(file.Path);
                break;
            case FileRole.Hook:
            case FileRole.Util:
                folders.Utils.Add(file.Path);
                break;
            case FileRole.Style:
                folders.Styles.Add(file.Path);
                break;
            case FileRole.Api:
                folders.Api.Add(file.Path);
                break;
            case FileRole.Database:
                folders.Data.Add(file.Path);
                break;
        }
    }

    // Connects files whose relative imports resolve to another indexed file.
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
            if (segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0)
            {
                segments.Pop();
            }
            else if (segment != "..")
            {
                segments.Push(segment);
            }
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
