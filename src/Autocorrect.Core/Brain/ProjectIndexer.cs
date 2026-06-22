namespace Autocorrect.Core.Brain;

public sealed class IndexOptions
{
    public int MaxFileSizeBytes { get; set; } = 200 * 1024;
    public int MaxFiles { get; set; } = 1500;
    public int MaxChunksPerFile { get; set; } = 24;
    public int MaxInitialChunks { get; set; } = 8000;
    public HashSet<string> IgnoredFolders { get; set; } = DefaultIgnoredFolders();

    public static HashSet<string> DefaultIgnoredFolders() => new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", "dist", "build", ".next", "out", "coverage",
        ".turbo", ".cache", ".vercel", "bin", "obj", ".vs", ".idea", ".vscode",
        "vendor", "__pycache__", "generated"
    };
}

public interface IProjectIndexer
{
    ProjectBrainData Index(string projectRoot, IndexOptions options);
}

// Coordinates project scanning, framework analysis, chunking, and graph/rule extraction.
public sealed class ProjectIndexer : IProjectIndexer
{
    private readonly IProjectScanner _scanner;
    private readonly IProjectAnalyzer _analyzer;
    private readonly IProjectChunker _chunker;

    public ProjectIndexer()
        : this(new ProjectScanner(), new ProjectAnalyzer(), new ProjectChunker())
    {
    }

    public ProjectIndexer(IProjectScanner scanner, IProjectAnalyzer analyzer, IProjectChunker chunker)
    {
        _scanner = scanner;
        _analyzer = analyzer;
        _chunker = chunker;
    }

    public ProjectBrainData Index(string projectRoot, IndexOptions options) => IndexDetailed(projectRoot, options).Brain;

    public ProjectIndexSnapshot IndexDetailed(string projectRoot, IndexOptions options)
    {
        var files = _scanner.Scan(projectRoot, options, out var skipped);
        var brain = _analyzer.Analyze(projectRoot, files, options);
        var extractor = new TreeSitterExtractor();
        var extractions = new List<GraphExtractionResult>();
        foreach (var source in files)
        {
            var extraction = extractor.TryExtract(source.RelativePath, source.Content);
            if (extraction is not null)
            {
                extractions.Add(extraction);
            }
        }

        GraphExtractionMerger.Apply(brain, extractions);
        var summariesByPath = brain.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var chunks = new List<ProjectChunk>();
        var notEmbedded = new List<SkippedFile>();

        foreach (var source in files)
        {
            if (!summariesByPath.TryGetValue(source.RelativePath, out var summary))
            {
                continue;
            }

            if (chunks.Count >= options.MaxInitialChunks)
            {
                notEmbedded.Add(new SkippedFile { Path = source.RelativePath, Reason = $"chunk limit reached ({options.MaxInitialChunks})" });
                continue;
            }

            chunks.AddRange(_chunker.Chunk(source, summary, options.MaxChunksPerFile));
        }

        var allSkipped = skipped.Concat(notEmbedded).ToList();
        return new ProjectIndexSnapshot
        {
            Brain = brain,
            Chunks = chunks,
            SkippedFiles = allSkipped.Count,
            SkippedDetails = allSkipped
        };
    }
}
