using System;
using System.IO;
using System.Linq;
using System.Threading;
using Autocorrect.Core;
using Autocorrect.Core.Brain;

var tests = new (string Name, Action Run)[]
{
    ("brain indexer detects stack and ignores secrets", BrainIndexerDetectsStackAndIgnoresSecrets),
    ("brain detailed index creates hashed chunks", BrainDetailedIndexCreatesHashedChunks),
    ("markdown chunker splits by headings", MarkdownChunkerSplitsByHeadings),
    ("project folder change detector finds edits", ProjectFolderChangeDetectorFindsEdits),
    ("prompt compiler fallback does not invent files", PromptCompilerFallbackDoesNotInventFiles),
    ("token efficient prompt assembler formats line hits", TokenEfficientPromptAssemblerFormatsLineHits),
    ("sqlite vector store round-trips and ranks by cosine", SqliteVectorStoreRoundTripsAndRanks),
    ("csharp ast chunker extracts class and method", CsharpAstChunkerExtractsSymbols),
    ("graph retriever finds import neighbors", GraphRetrieverFindsImportNeighbors),
    ("graph symbol traverser follows call edges", GraphSymbolTraverserFollowsCallEdges),
    ("graph extraction merger stores line metadata", GraphExtractionMergerStoresLineMetadata),
    ("architecture indexer finds hub files", ArchitectureIndexerFindsHubFiles),
    ("entry point indexer detects program cs", EntryPointIndexerDetectsProgramCs),
    ("cross file call graph links symbols", CrossFileCallGraphLinksSymbols),
    ("architecture community indexer groups files", ArchitectureCommunityIndexerGroupsFiles),
    ("tree-sitter content preparer extracts vue script", TreeSitterContentPreparerExtractsVueScript),
    ("symbol graph persists in sqlite", SymbolGraphPersistsInSqlite),
    ("tree-sitter extracts typescript symbols", TreeSitterExtractsTypescriptSymbols),
    ("prompt symbol parser finds qualified names", PromptSymbolParserFindsQualifiedNames),
    ("prompt symbol resolver maps to graph nodes", PromptSymbolResolverMapsToGraphNodes),
    ("wordpiece tokenizer wraps with cls and sep", WordPieceTokenizerWrapsWithClsAndSep),
    ("brain retrieval finds login file for animation prompt", BrainRetrievalFindsLoginFile),
    ("prompt analyzer flags vague prompt", PromptAnalyzerFlagsVaguePrompt),
    ("smart rewriter works without ollama", SmartRewriterWorksWithoutOllama),
    ("secret scanner redacts keys", SecretScannerRedactsKeys),
    ("custom dictionary fixes component typo", CustomDictionaryFixesComponentTypo),
    ("symspell fixes common non-config typo", SymSpellFixesCommonNonConfigTypo),
    ("built-in fixes recent user typo examples", BuiltInFixesRecentUserTypoExamples),
    ("fixes repeated letter typing mistakes", FixesRepeatedLetterTypingMistakes),
    ("fixes short transposition words", FixesShortTranspositionWords),
    ("splits merged words", SplitsMergedWords),
    ("scrambled known word fixes to python", ScrambledKnownWordFixesToPython),
    ("fixes heavy jumbled words", FixesHeavyJumbledWords),
    ("learned personal word wins ranking", LearnedPersonalWordWinsRanking),
    ("context model scores known transitions", ContextModelScoresKnownTransitions),
    ("learned bigram raises context affinity", LearnedBigramRaisesContextAffinity),
    ("suggestions include correction and learned words", SuggestionsIncludeCorrectionAndLearnedWords),
    ("protected vocabulary is not corrected", ProtectedVocabularyIsNotCorrected),
    ("unsafe tokens are not corrected", UnsafeTokensAreNotCorrected),
    ("correct words are left alone", CorrectWordsAreLeftAlone),
    ("ignored words are left alone", IgnoredWordsAreLeftAlone),
    ("replacement preserves title casing", ReplacementPreservesTitleCasing),
    ("engine stays fast after warmup", EngineStaysFastAfterWarmup),
    ("active ide resolver parses cursor title", ActiveIdeResolverParsesCursorTitle),
    ("active ide resolver ignores settings in ide mode", ActiveIdeResolverIgnoresSettingsFallbackInIde)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex.Message);
    }
}

if (failures > 0)
{
    Environment.ExitCode = 1;
}

static void CustomDictionaryFixesComponentTypo()
{
    var result = Correct("compomment");

    Assert.NotNull(result);
    Assert.Equal("component", result!.Replacement);
    Assert.True(result.Confidence >= 0.99, "Expected custom dictionary confidence.");
}

static void SymSpellFixesCommonNonConfigTypo()
{
    var result = Correct("acommodation");

    Assert.NotNull(result);
    Assert.Equal("accommodation", result!.Replacement);
    Assert.True(result.Reason.Contains("symspell", StringComparison.OrdinalIgnoreCase), "Expected indexed spell engine.");
}

static void BuiltInFixesRecentUserTypoExamples()
{
    var examples = new Dictionary<string, string>
    {
        ["dotn"] = "don't",
        ["pythoin"] = "python",
        ["tensorfloe"] = "tensorflow",
        ["soemthgin"] = "something",
        ["confgiartiuon"] = "configuration",
        ["nmsiektae"] = "mistake",
        ["wroids"] = "words",
        ["setgigna"] = "settings",
        ["coplme"] = "complete"
    };

    foreach (var (typo, expected) in examples)
    {
        var result = Correct(typo);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Replacement);
    }
}

static void FixesRepeatedLetterTypingMistakes()
{
    var examples = new Dictionary<string, string>
    {
        ["wrtiee"] = "write",
        ["writtee"] = "write",
        ["goood"] = "good"
    };

    foreach (var (typo, expected) in examples)
    {
        var result = Correct(typo);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Replacement);
    }
}

static void FixesShortTranspositionWords()
{
    var result = Correct("ti");

    Assert.NotNull(result);
    Assert.Equal("it", result!.Replacement);
}

static void SplitsMergedWords()
{
    var result = Correct("howthsi");

    Assert.NotNull(result);
    Assert.Equal("how this", result!.Replacement);
}

static void ScrambledKnownWordFixesToPython()
{
    var result = Correct("ptyonh");

    Assert.NotNull(result);
    Assert.Equal("python", result!.Replacement);
}

static void FixesHeavyJumbledWords()
{
    foreach (var (typo, expected) in new[] { ("usspeod", "supposed"), ("idffrence", "difference") })
    {
        var result = Correct(typo);
        Assert.NotNull(result);
        Assert.Equal(expected, result!.Replacement);
    }
}

static void LearnedPersonalWordWinsRanking()
{
    var settings = new CorrectionSettings
    {
        LearnWordAfterCount = 3,
        LearnedWordFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bro"] = 9
        }
    };
    var engine = new LocalCorrectionEngine();

    var learnedWord = engine.Correct(
        new CorrectionRequest("bro", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);
    var typo = engine.Correct(
        new CorrectionRequest("bor", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);

    Assert.Null(learnedWord);
    Assert.NotNull(typo);
    Assert.Equal("bro", typo!.Replacement);
}

static void SuggestionsIncludeCorrectionAndLearnedWords()
{
    var settings = new CorrectionSettings
    {
        LearnedWordFrequencies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bro"] = 5
        }
    };
    var engine = new LocalCorrectionEngine();

    var pythonSuggestions = engine.Suggest("ptyonh", Array.Empty<string>(), settings);
    var personalSuggestions = engine.Suggest("br", Array.Empty<string>(), settings);

    Assert.True(pythonSuggestions.Any(s => s.Text.Equals("python", StringComparison.OrdinalIgnoreCase)), "Expected python suggestion.");
    Assert.True(personalSuggestions.Any(s => s.Text.Equals("bro", StringComparison.OrdinalIgnoreCase)), "Expected learned bro suggestion.");
}

static void ProtectedVocabularyIsNotCorrected()
{
    var settings = new CorrectionSettings();
    settings.ProtectedVocabulary.Add("Promto");
    var result = new LocalCorrectionEngine().Correct(
        new CorrectionRequest("Promto", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);

    Assert.Null(result);
}

static void UnsafeTokensAreNotCorrected()
{
    Assert.Null(Correct("myFileName"));
    Assert.Null(Correct("abc123"));
}

static void CorrectWordsAreLeftAlone()
{
    Assert.Null(Correct("component"));
}

static void IgnoredWordsAreLeftAlone()
{
    var settings = new CorrectionSettings();
    settings.IgnoredWords.Add("compomment");

    var result = new LocalCorrectionEngine().Correct(
        new CorrectionRequest("compomment", Array.Empty<string>(), AppContextSnapshot.Unknown),
        settings);

    Assert.Null(result);
}

static void ReplacementPreservesTitleCasing()
{
    var result = Correct("Compomment");

    Assert.NotNull(result);
    Assert.Equal("Component", result!.Replacement);
}

static void EngineStaysFastAfterWarmup()
{
    SymSpellCorrectionEngine.WarmUp();
    var engine = new LocalCorrectionEngine();
    var settings = new CorrectionSettings();
    var words = new[] { "acommodation", "recieve", "keybroad", "compomment", "pythoin", "soemthgin" };
    var started = DateTimeOffset.UtcNow;

    for (var i = 0; i < 500; i++)
    {
        var word = words[i % words.Length];
        _ = engine.Correct(new CorrectionRequest(word, Array.Empty<string>(), AppContextSnapshot.Unknown), settings);
    }

    var elapsed = DateTimeOffset.UtcNow - started;
    Assert.True(elapsed.TotalMilliseconds < 1000, $"Expected 500 warm lookups under 1000 ms, got {elapsed.TotalMilliseconds:0.0} ms.");
}

static void ContextModelScoresKnownTransitions()
{
    var model = ContextLanguageModel.Default;
    var settings = new CorrectionSettings();

    Assert.True(model.Affinity(new[] { "i" }, "want", settings) > 0.5, "Expected 'i want' to score highly.");
    Assert.True(model.Affinity(new[] { "qqqq" }, "want", settings) == 0, "Unknown previous word should score zero.");
    Assert.True(model.Affinity(Array.Empty<string>(), "want", settings) == 0, "No context should score zero.");
}

static void LearnedBigramRaisesContextAffinity()
{
    var model = ContextLanguageModel.Default;
    var settings = new CorrectionSettings();

    var before = model.Affinity(new[] { "video" }, "kling", settings);
    settings.LearnedBigrams[ContextLanguageModel.BigramKey("video", "kling")] = 4;
    var after = model.Affinity(new[] { "video" }, "kling", settings);

    Assert.True(before == 0, "Unknown personal pair should start at zero.");
    Assert.True(after > 0.8, "Learned personal pair should raise context affinity.");
}

static CorrectionResult? Correct(string word)
{
    return new LocalCorrectionEngine().Correct(
        new CorrectionRequest(word, Array.Empty<string>(), AppContextSnapshot.Unknown),
        new CorrectionSettings());
}

static string CreateSampleProject()
{
    var root = Path.Combine(Path.GetTempPath(), "brain-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(root, "src", "components"));
    Directory.CreateDirectory(Path.Combine(root, "src", "app"));

    File.WriteAllText(Path.Combine(root, "package.json"),
        "{\"name\":\"sample\",\"dependencies\":{\"react\":\"18.0.0\",\"tailwindcss\":\"3.4.0\",\"gsap\":\"3.12.0\"}}");
    File.WriteAllText(Path.Combine(root, "tailwind.config.js"), "module.exports = { content: [] };");
    File.WriteAllText(Path.Combine(root, "src", "components", "LoginForm.tsx"),
        "import gsap from 'gsap';\nexport function LoginForm() {\n  gsap.to('.btn', { opacity: 1 });\n  return null;\n}\n");
    File.WriteAllText(Path.Combine(root, "src", "app", "page.tsx"),
        "export default function Page() { return null; }\n");
    File.WriteAllText(Path.Combine(root, "src", "config.ts"),
        "export const token = \"sk-abcdef123456789\";\n");
    File.WriteAllText(Path.Combine(root, ".env"), "API_KEY=sk-supersecretvalue1234\n");
    return root;
}

static void BrainIndexerDetectsStackAndIgnoresSecrets()
{
    var root = CreateSampleProject();
    try
    {
        var brain = new ProjectIndexer().Index(root, new IndexOptions());

        Assert.Equal("React", brain.Stack.Framework);
        Assert.Equal("Tailwind CSS", brain.Stack.Styling);
        Assert.True(brain.Rules.Any(r => r.Contains("GSAP", StringComparison.OrdinalIgnoreCase)), "Expected GSAP rule.");
        Assert.True(brain.Files.All(f => !f.Path.EndsWith(".env", StringComparison.OrdinalIgnoreCase)), ".env must be ignored.");
        Assert.True(brain.Files.All(f => f.PreviewChunks.All(c => !c.Contains("abcdef123456789", StringComparison.Ordinal))), "Secrets must be redacted.");
    }
    finally
    {
        TryDelete(root);
    }
}

static void BrainRetrievalFindsLoginFile()
{
    var root = CreateSampleProject();
    var brainBase = Path.Combine(Path.GetTempPath(), "brain-store-" + Guid.NewGuid().ToString("N")[..8]);
    try
    {
        var brain = new ProjectIndexer().Index(root, new IndexOptions());
        var store = new FileVectorStore(brainBase, root, new KeywordEmbeddingProvider());
        store.AddDocumentsAsync(brain.Files.Select(f => new VectorDocument
        {
            Id = f.Path,
            Path = f.Path,
            Role = f.Role,
            Summary = f.Summary,
            Text = $"{f.Path} {f.Summary} {string.Join(' ', f.Symbols)}"
        }), CancellationToken.None).GetAwaiter().GetResult();

        var hits = store.SearchAsync("fix the login animation", 6, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(hits.Any(h => h.Document.Path.Contains("LoginForm", StringComparison.OrdinalIgnoreCase)), "Expected LoginForm in retrieval.");
    }
    finally
    {
        TryDelete(root);
        TryDelete(brainBase);
    }
}

static void BrainDetailedIndexCreatesHashedChunks()
{
    var root = CreateSampleProject();
    try
    {
        var snapshot = new ProjectIndexer().IndexDetailed(root, new IndexOptions());

        Assert.True(snapshot.Brain.Files.Count > 0, "Expected indexed files.");
        Assert.True(snapshot.Chunks.Count > 0, "Expected project chunks.");
        Assert.True(snapshot.Chunks.All(chunk => !string.IsNullOrWhiteSpace(chunk.FileHash)), "Expected file hashes.");
        Assert.True(snapshot.Chunks.All(chunk => !string.IsNullOrWhiteSpace(chunk.ChunkHash)), "Expected chunk hashes.");
        Assert.True(snapshot.Chunks.Any(chunk => chunk.FilePath.Contains("LoginForm", StringComparison.OrdinalIgnoreCase)), "Expected LoginForm chunk.");
        Assert.True(snapshot.Brain.Files.All(file => !file.Path.EndsWith(".env", StringComparison.OrdinalIgnoreCase)), ".env must be ignored.");
    }
    finally
    {
        TryDelete(root);
    }
}

static void MarkdownChunkerSplitsByHeadings()
{
    var root = Path.Combine(Path.GetTempPath(), "chunk-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(root);
    var path = Path.Combine(root, "README.md");
    File.WriteAllText(path, "# Auth\nLogin details\n\n## Billing\nPayment details\n");

    try
    {
        var scanner = new ProjectScanner();
        var files = scanner.Scan(root, new IndexOptions(), out _);
        var brain = new ProjectAnalyzer().Analyze(root, files, new IndexOptions());
        var source = files.Single(file => file.RelativePath == "README.md");
        var summary = brain.Files.Single(file => file.Path == "README.md");
        var chunks = new ProjectChunker().Chunk(source, summary, 10);

        Assert.True(chunks.Count >= 2, "Expected heading chunks.");
        Assert.True(chunks.Any(chunk => chunk.Symbol.Contains("Auth", StringComparison.OrdinalIgnoreCase)), "Expected Auth heading chunk.");
    }
    finally
    {
        TryDelete(root);
    }
}

static void ProjectFolderChangeDetectorFindsEdits()
{
    var root = Path.Combine(Path.GetTempPath(), "woody_sync_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    var file = Path.Combine(root, "app.ts");
    File.WriteAllText(file, "export const version = 1;");
    try
    {
        var options = new IndexOptions { MaxFiles = 50 };
        var scanner = new ProjectScanner();
        var scanned = scanner.Scan(root, options, out _);
        var hash = scanned[0].Hash;
        var previous = new ProjectIndexMetadata
        {
            FileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["app.ts"] = hash
            }
        };

        var unchanged = ProjectFolderChangeDetector.Detect(root, options, previous);
        Assert.False(unchanged.HasChanges, "Expected no changes for same file content.");

        File.WriteAllText(file, "export const version = 2;");
        var changed = ProjectFolderChangeDetector.Detect(root, options, previous);
        Assert.True(changed.Modified.Count == 1, "Expected one modified file.");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void PromptCompilerFallbackDoesNotInventFiles()
{
    var compiler = new PromptCompilerService(new FakeOllamaClient());
    var result = compiler.CompileAsync(new PromptCompilerRequest
    {
        OriginalPrompt = "fix the login thing and maek it better",
        TargetAgent = PromptTargetAgent.Codex,
        Brain = new ProjectBrainData
        {
            Stack = new ProjectStack { Framework = "Next.js", Language = "TypeScript" }
        },
        Retrieval = new RetrievalResponse
        {
            RetrievalMode = RetrievalMode.KeywordFallback,
            Results = new List<RetrievalResult>
            {
                new() { FilePath = "src/components/LoginForm.tsx", Score = 0.91, ContentPreview = "LoginForm component" },
                new() { FilePath = "src/lib/auth.ts", Score = 0.86, ContentPreview = "auth helpers" }
            }
        }
    }, writerAvailable: false, CancellationToken.None).GetAwaiter().GetResult();

    Assert.True(result.OptimizedPrompt.Contains("src/components/LoginForm.tsx", StringComparison.Ordinal), "Expected real retrieved file.");
    Assert.True(result.OptimizedPrompt.Contains("src/lib/auth.ts", StringComparison.Ordinal), "Expected real retrieved file.");
    Assert.False(result.OptimizedPrompt.Contains("src/app/api/auth/route.ts", StringComparison.Ordinal), "Must not invent files.");
    Assert.False(result.OptimizedPrompt.Contains("Goal:", StringComparison.Ordinal), "Verbose Goal header should not appear.");
    var filesIndex = result.OptimizedPrompt.IndexOf("Read at files:", StringComparison.Ordinal);
    Assert.True(filesIndex > 0, "Compact prompt should be followed by Read at files section.");
    Assert.True(filesIndex > result.OptimizedPrompt.IndexOf("login", StringComparison.OrdinalIgnoreCase), "Prompt body should come before the file list.");
}

static void TokenEfficientPromptAssemblerFormatsLineHits()
{
    var assembled = TokenEfficientPromptAssembler.Assemble(
        "Find where BOS is configured, including UI and settings.",
        new PromptCompilerRequest
        {
            OriginalPrompt = "where is bos configured in the app",
            TargetAgent = PromptTargetAgent.Codex,
            Retrieval = new RetrievalResponse
            {
                Results = new List<RetrievalResult>
                {
                    new() { FilePath = "src/BosConfig.cs", Symbol = "BosSettings", StartLine = 12, EndLine = 40, Score = 0.95 },
                    new() { FilePath = "src/BosUi.xaml", Symbol = "BosPanel", StartLine = 5, EndLine = 18, Score = 0.91 },
                    new() { FilePath = "src/BosConfig.cs", Symbol = "ApplyBos", StartLine = 55, EndLine = 72, Score = 0.88 }
                }
            }
        },
        PromptTargetAgent.Codex);

    Assert.True(assembled.Contains("Read at files:", StringComparison.Ordinal));
    Assert.True(assembled.Contains("src/BosConfig.cs:12-40 (BosSettings)", StringComparison.Ordinal));
    Assert.True(assembled.Contains("src/BosUi.xaml:5-18 (BosPanel)", StringComparison.Ordinal));
    Assert.False(assembled.Contains("Goal:", StringComparison.Ordinal));
}

static void SqliteVectorStoreRoundTripsAndRanks()
{
    var baseDir = Path.Combine(Path.GetTempPath(), "woody_sqlite_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(baseDir);
    try
    {
        using var store = new SqliteVectorStore(baseDir);
        const string collection = "woody_project_test";
        Assert.True(store.EnsureCollectionAsync(collection, 3, CancellationToken.None).GetAwaiter().GetResult(), "Collection should initialize.");

        var chunks = new List<ProjectChunk>
        {
            new() { Id = "a", FilePath = "src/a.cs", FileName = "a.cs", Content = "alpha", ContentPreview = "alpha" },
            new() { Id = "b", FilePath = "src/b.cs", FileName = "b.cs", Content = "beta", ContentPreview = "beta" }
        };
        var vectors = new List<float[]>
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f }
        };
        store.UpsertAsync(collection, chunks, vectors, CancellationToken.None).GetAwaiter().GetResult();

        var stats = store.GetStatsAsync(collection, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(stats.IsAvailable, "SQLite store should be available.");
        Assert.Equal(2L, stats.VectorCount);
        Assert.Equal(3, stats.VectorDimension);

        var results = store.SearchAsync(collection, new[] { 0.9f, 0.1f, 0f }, 5, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(results.Count > 0, "Search should return results.");
        Assert.Equal("src/a.cs", results[0].FilePath);
    }
    finally
    {
        try { Directory.Delete(baseDir, true); } catch { }
    }
}

static void CsharpAstChunkerExtractsSymbols()
{
    const string code = """
        namespace Demo;

        public class LoginService
        {
            public bool Validate(string user)
            {
                return !string.IsNullOrWhiteSpace(user);
            }
        }
        """;

    var ranges = CsharpAstChunker.Extract(code, "LoginService.cs");
    Assert.True(ranges.Any(range => range.Symbol == "LoginService"), "Expected LoginService class.");
    Assert.True(ranges.Any(range => range.Symbol == "Validate"), "Expected Validate method.");
}

static void GraphRetrieverFindsImportNeighbors()
{
    var brain = new ProjectBrainData();
    brain.Graph.AddNode("file:src/a.ts", NodeType.File, "a.ts", "src/a.ts");
    brain.Graph.AddNode("file:src/b.ts", NodeType.File, "b.ts", "src/b.ts");
    brain.Graph.AddEdge("file:src/a.ts", "file:src/b.ts", EdgeType.Imports);

    var seeds = new List<RetrievalResult>
    {
        new() { FilePath = "src/a.ts", Score = 0.9 }
    };

    var neighbors = GraphRetriever.NeighborFilePaths(brain, seeds);
    Assert.True(neighbors.Contains("src/b.ts"), "Expected imported neighbor file.");
}

static void GraphSymbolTraverserFollowsCallEdges()
{
    var brain = new ProjectBrainData();
    brain.Graph.UpsertNode("sym:src/A.cs:Run:10", NodeType.Function, "Run", "src/A.cs", new Dictionary<string, string> { ["startLine"] = "10", ["endLine"] = "20" });
    brain.Graph.UpsertNode("sym:src/B.cs:Apply:30", NodeType.Function, "Apply", "src/B.cs", new Dictionary<string, string> { ["startLine"] = "30", ["endLine"] = "45" });
    brain.Graph.AddEdge("sym:src/A.cs:Run:10", "sym:src/B.cs:Apply:30", EdgeType.Calls);

    var hits = GraphSymbolTraverser.Traverse(brain, new List<RetrievalResult>
    {
        new() { FilePath = "src/A.cs", Symbol = "Run", Score = 0.95 }
    });

    Assert.True(hits.Any(hit => hit.FilePath == "src/B.cs" && hit.Symbol == "Apply" && hit.StartLine == 30), "Expected call-graph neighbor with line metadata.");
}

static void GraphExtractionMergerStoresLineMetadata()
{
    var brain = new ProjectBrainData
    {
        Files =
        {
            new ProjectFileSummary { Path = "src/Demo.cs", Summary = "demo" }
        }
    };

    GraphExtractionMerger.Apply(brain, new List<GraphExtractionResult>
    {
        new()
        {
            SourceFile = "src/Demo.cs",
            Nodes =
            {
                new ExtractionNode
                {
                    Id = "sym:src/Demo.cs:BosSettings:12",
                    Label = "BosSettings",
                    SourceFile = "src/Demo.cs",
                    Kind = "class",
                    StartLine = 12,
                    EndLine = 40
                }
            }
        }
    });

    var node = brain.Graph.Nodes.Single(item => item.Label == "BosSettings");
    Assert.Equal("12", node.Meta["startLine"]);
    Assert.Equal("40", node.Meta["endLine"]);
}

static void ArchitectureIndexerFindsHubFiles()
{
    var brain = new ProjectBrainData
    {
        Files =
        {
            new ProjectFileSummary { Path = "src/Core.cs", Role = FileRole.Util },
            new ProjectFileSummary { Path = "src/Api.cs", Role = FileRole.Api }
        }
    };

    brain.Graph.UpsertNode("sym:src/Core.cs:Run:1", NodeType.Function, "Run", "src/Core.cs", new Dictionary<string, string> { ["startLine"] = "1", ["endLine"] = "10" });
    brain.Graph.UpsertNode("sym:src/Api.cs:Handle:1", NodeType.Function, "Handle", "src/Api.cs", new Dictionary<string, string> { ["startLine"] = "1", ["endLine"] = "10" });
    brain.Graph.AddEdge("sym:src/Core.cs:Run:1", "sym:src/Api.cs:Handle:1", EdgeType.Calls);
    brain.Graph.AddEdge("file:src/Core.cs", "sym:src/Core.cs:Run:1", EdgeType.Contains);

    var profile = ProjectArchitectureIndexer.Build(brain);
    Assert.True(profile.Hubs.Any(hub => hub.FilePath == "src/Core.cs"), "Expected highly connected file to become an architecture hub.");
}

static void EntryPointIndexerDetectsProgramCs()
{
    var brain = new ProjectBrainData
    {
        Files =
        {
            new ProjectFileSummary { Path = "src/Program.cs", Importance = 0.9 },
            new ProjectFileSummary { Path = "src/Utils.cs", Importance = 0.4 }
        }
    };

    var points = EntryPointIndexer.Detect(brain);
    Assert.True(points.Any(point => point.FilePath == "src/Program.cs"), "Expected Program.cs entry point.");
}

static void CrossFileCallGraphLinksSymbols()
{
    var brain = new ProjectBrainData
    {
        Files =
        {
            new ProjectFileSummary { Path = "src/A.cs" },
            new ProjectFileSummary { Path = "src/B.cs" }
        }
    };

    brain.Graph.UpsertNode("sym:src/A.cs:Run:1", NodeType.Function, "Run", "src/A.cs", new Dictionary<string, string> { ["kind"] = "method", ["startLine"] = "1", ["endLine"] = "8" });
    brain.Graph.UpsertNode("sym:src/B.cs:Apply:1", NodeType.Function, "Apply", "src/B.cs", new Dictionary<string, string> { ["kind"] = "method", ["startLine"] = "1", ["endLine"] = "8" });
    brain.Graph.UpsertNode("call:src/A.cs:Apply:4", NodeType.Function, "Apply", "src/A.cs", new Dictionary<string, string> { ["kind"] = "call" });
    brain.Graph.AddEdge("sym:src/A.cs:Run:1", "call:src/A.cs:Apply:4", EdgeType.Calls);

    CrossFileCallGraphResolver.Apply(brain);

    Assert.True(brain.Graph.Edges.Any(edge =>
        edge.Type == EdgeType.Calls &&
        edge.From == "sym:src/A.cs:Run:1" &&
        edge.To == "sym:src/B.cs:Apply:1"), "Expected cross-file symbol call edge.");
    Assert.True(brain.Graph.Edges.Any(edge =>
        edge.Type == EdgeType.Calls &&
        edge.From == "file:src/A.cs" &&
        edge.To == "file:src/B.cs"), "Expected cross-file edge.");
}

static void ArchitectureCommunityIndexerGroupsFiles()
{
    var brain = new ProjectBrainData
    {
        Files =
        {
            new ProjectFileSummary { Path = "src/a/One.cs", Role = FileRole.Util },
            new ProjectFileSummary { Path = "src/a/Two.cs", Role = FileRole.Util },
            new ProjectFileSummary { Path = "src/b/Other.cs", Role = FileRole.Api }
        }
    };

    brain.Graph.AddNode("file:src/a/One.cs", NodeType.File, "One.cs", "src/a/One.cs");
    brain.Graph.AddNode("file:src/a/Two.cs", NodeType.File, "Two.cs", "src/a/Two.cs");
    brain.Graph.AddNode("file:src/b/Other.cs", NodeType.File, "Other.cs", "src/b/Other.cs");
    brain.Graph.AddEdge("file:src/a/One.cs", "file:src/a/Two.cs", EdgeType.Imports);

    var communities = ArchitectureCommunityIndexer.Build(brain);
    Assert.True(communities.Any(community => community.FilePaths.Count >= 2), "Expected connected files in one community.");
}

static void TreeSitterContentPreparerExtractsVueScript()
{
    const string vue = """
        <template><div /></template>
        <script lang="ts">
        export function mount() {}
        </script>
        """;

    var prepared = TreeSitterContentPreparer.Prepare("src/App.vue", vue);
    Assert.NotNull(prepared);
    Assert.Equal("typescript", prepared!.LanguageKey);
    Assert.True(prepared.Content.Contains("mount", StringComparison.Ordinal), "Expected vue script body.");
}

static void SymbolGraphPersistsInSqlite()
{
    var baseDir = Path.Combine(Path.GetTempPath(), "woody_graph_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(baseDir);
    try
    {
        using var store = new SqliteVectorStore(baseDir);
        const string collection = "woody_project_graph";
        store.EnsureCollectionAsync(collection, 3, CancellationToken.None).GetAwaiter().GetResult();

        var graph = new ProjectGraph();
        graph.AddNode("file:src/a.cs", NodeType.File, "a.cs", "src/a.cs");
        graph.AddNode("file:src/b.cs", NodeType.File, "b.cs", "src/b.cs");
        graph.AddEdge("file:src/a.cs", "file:src/b.cs", EdgeType.Imports);
        store.UpsertSymbolGraphAsync(collection, graph, CancellationToken.None).GetAwaiter().GetResult();

        var stats = store.GetSymbolGraphStatsAsync(collection, CancellationToken.None).GetAwaiter().GetResult();
        Assert.Equal(2, stats.Nodes);
        Assert.Equal(1, stats.Edges);
    }
    finally
    {
        try { Directory.Delete(baseDir, true); } catch { }
    }
}

static void TreeSitterExtractsTypescriptSymbols()
{
    const string code = """
        class LoginForm {
          refresh() {
            return true;
          }
        }
        """;

    var extraction = new TreeSitterExtractor().TryExtract("src/LoginForm.js", code);
    Assert.NotNull(extraction);
    Assert.True(extraction!.Nodes.Any(node => node.Label == "LoginForm"), "Expected LoginForm class node.");
}

static void PromptSymbolParserFindsQualifiedNames()
{
    var parsed = PromptSymbolParser.Parse("fix DashboardWindow.RefreshAsync vector db in src/app/page.tsx");
    Assert.True(parsed.TypeNames.Contains("DashboardWindow"), "Expected DashboardWindow type.");
    Assert.True(parsed.MethodNames.Contains("RefreshAsync"), "Expected RefreshAsync method.");
    Assert.True(parsed.FilePaths.Any(path => path.Contains("page.tsx", StringComparison.OrdinalIgnoreCase)), "Expected file path.");
}

static void PromptSymbolResolverMapsToGraphNodes()
{
    var brain = new ProjectBrainData();
    brain.Files.Add(new ProjectFileSummary
    {
        Path = "src/DashboardWindow.xaml.cs",
        Symbols = new List<string> { "DashboardWindow", "RefreshAsync" }
    });
    brain.Graph.AddNode("sym:src/DashboardWindow.xaml.cs:RefreshAsync", NodeType.Function, "RefreshAsync", "src/DashboardWindow.xaml.cs");

    var parsed = PromptSymbolParser.Parse("fix DashboardWindow.RefreshAsync");
    var targets = SymbolTargetResolver.Resolve(parsed, brain);
    Assert.True(targets.Any(target => target.Symbol == "RefreshAsync"), "Expected RefreshAsync target.");
}

static void WordPieceTokenizerWrapsWithClsAndSep()
{
    var vocabPath = Path.Combine(Path.GetTempPath(), "woody_vocab_" + Guid.NewGuid().ToString("N") + ".txt");
    File.WriteAllLines(vocabPath, new[] { "[PAD]", "[UNK]", "[CLS]", "[SEP]", "hello", "world" });
    try
    {
        var tokenizer = new WordPieceTokenizer(vocabPath, 64);
        var (ids, mask) = tokenizer.Encode("hello world");

        Assert.Equal(4, ids.Length);
        Assert.Equal(2L, ids[0]);
        Assert.Equal(4L, ids[1]);
        Assert.Equal(5L, ids[2]);
        Assert.Equal(3L, ids[^1]);
        Assert.Equal(ids.Length, mask.Length);
        Assert.True(mask.All(value => value == 1L), "Attention mask should be all ones for unpadded input.");
    }
    finally
    {
        try { File.Delete(vocabPath); } catch { }
    }
}

static void PromptAnalyzerFlagsVaguePrompt()
{
    var analysis = new PromptAnalyzer().Analyze(
        "make this page better",
        null,
        Array.Empty<RetrievedFile>(),
        new UserPreferences());

    Assert.True(analysis.ShouldAskUserForMoreInfo || analysis.MissingContext.Count > 0, "Vague prompt should request more info.");
}

static void SmartRewriterWorksWithoutOllama()
{
    var root = CreateSampleProject();
    try
    {
        var brain = new ProjectIndexer().Index(root, new IndexOptions());
        var retrieved = brain.Files
            .Where(f => f.Path.Contains("LoginForm", StringComparison.OrdinalIgnoreCase))
            .Select(f => new RetrievedFile { File = f, Reason = "keyword match", Score = 1 })
            .ToList();
        var analysis = new PromptAnalyzer().Analyze("fix the login animation", brain, retrieved, new UserPreferences());

        var result = new SmartPromptRewriter(null)
            .BuildAsync("fix the login animation", analysis, brain, retrieved, new UserPreferences(), false, CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.ImprovedPrompt.Contains("Task:", StringComparison.Ordinal), "Structured prompt expected.");
        Assert.True(result.ImprovedPrompt.Contains("Instructions:", StringComparison.Ordinal), "Instructions section expected.");
        Assert.False(result.UsedOllama);
    }
    finally
    {
        TryDelete(root);
    }
}

static void SecretScannerRedactsKeys()
{
    Assert.True(SecretScanner.Redact("API_KEY=sk-1234567890abcdef").Contains("[REDACTED]", StringComparison.Ordinal), "API key should be redacted.");
    Assert.True(SecretScanner.Redact("postgres://user:pass@host:5432/db").Contains("[REDACTED]", StringComparison.Ordinal), "Database URL should be redacted.");
}

static void ActiveIdeResolverParsesCursorTitle()
{
    var indexed = new[] { @"C:\Users\Admin\Documents\New project 5", @"C:\Users\Admin\New folder (2)" };
    var session = ActiveIdeResolver.Resolve(
        "Cursor",
        "App.xaml.cs - New project 5 - Cursor",
        @"C:\Users\Admin\Documents\New project 5",
        indexed);

    Assert.Equal(CodingAgentKind.Cursor, session.Agent);
    Assert.True(!string.IsNullOrWhiteSpace(session.WorkspaceRoot));
    Assert.True(session.ResolutionSource is "ide-active-window" or "ide-storage" or "indexed-project" or "ide-recent-storage");
}

static void ActiveIdeResolverIgnoresSettingsFallbackInIde()
{
    var session = ActiveIdeResolver.Resolve(
        "Notepad",
        "Untitled - Notepad",
        @"C:\Users\Admin\Documents\New project 5",
        Array.Empty<string>());

    Assert.Equal(@"C:\Users\Admin\Documents\New project 5", session.WorkspaceRoot);
    Assert.Equal("settings", session.ResolutionSource);
}

static void TryDelete(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
    catch
    {
        // Temp cleanup is best-effort.
    }
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }

    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new InvalidOperationException(message ?? "Expected condition to be false.");
        }
    }

    public static void Null(object? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException($"Expected null, got {value}.");
        }
    }

    public static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected non-null value.");
        }
    }
}

internal sealed class FakeOllamaClient : IOllamaClient
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<string?> GenerateAsync(string prompt, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task<string?> GenerateAsync(string prompt, OllamaGenerateOptions options, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken) => Task.FromResult<float[]?>(null);
}
