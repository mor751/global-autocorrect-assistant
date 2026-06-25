using System.Text.Json;

namespace Autocorrect.Core.Brain;

public sealed class PromptCompilerService
{
    private readonly IOllamaClient _writer;

    public PromptCompilerService(IOllamaClient writer)
    {
        _writer = writer;
    }

    public async Task<PromptCompilerResult> CompileAsync(
        PromptCompilerRequest request,
        bool writerAvailable,
        CancellationToken cancellationToken)
    {
        var cleaned = CleanPrompt(request.OriginalPrompt);
        var deterministic = BuildDeterministic(request, cleaned);
        if (!writerAvailable)
        {
            deterministic.Warnings.Add("Gemma3 unavailable; used deterministic local template.");
            return deterministic;
        }

        var writerPrompt = BuildWriterPrompt(request, cleaned);
        var response = await _writer.GenerateAsync(writerPrompt, new OllamaGenerateOptions(0.15, 500), cancellationToken);
        var parsed = TryParseStructured(response);
        if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.OptimizedPrompt))
        {
            parsed.UsedWriterModel = true;
            parsed.RelevantFiles = FilterRealFiles(parsed.RelevantFiles, request.Retrieval.Results);
            if (parsed.RelevantFiles.Count == 0)
            {
                parsed.RelevantFiles = deterministic.RelevantFiles;
            }

            var body = StripWriterArtifacts(StripEmbeddedFileSection(parsed.OptimizedPrompt));
            if (body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 55)
            {
                body = CompactGemmaBody(body, cleaned);
            }

            parsed.OptimizedPrompt = TokenEfficientPromptAssembler.Assemble(body, request, request.TargetAgent);
            return parsed;
        }

        deterministic.Warnings.Add("Gemma3 response was not usable; used deterministic local template.");
        return deterministic;
    }

    public EnhancedPromptResult ToEnhancedPromptResult(PromptCompilerResult compiled, string originalPrompt)
    {
        return new EnhancedPromptResult
        {
            Kind = EnhancementKind.ImprovedPrompt,
            ImprovedPrompt = compiled.OptimizedPrompt,
            ShorterPrompt = BuildShorter(compiled),
            Task = compiled.Tasks.FirstOrDefault() ?? CleanPrompt(originalPrompt),
            OriginalIntent = CleanPrompt(originalPrompt),
            RelevantFiles = compiled.RelevantFiles,
            MissingContext = compiled.Warnings,
            EstimatedPromptTokenChange = EstimateTokens(compiled.OptimizedPrompt) - EstimateTokens(originalPrompt),
            EstimatedReducedRetries = compiled.RelevantFiles.Count > 0 ? 1.4 : 0.5,
            Confidence = compiled.RelevantFiles.Count > 0 ? 0.82 : 0.62,
            UsedOllama = compiled.UsedWriterModel
        };
    }

    private const int MaxRelevantFiles = 8;

    private static PromptCompilerResult BuildDeterministic(PromptCompilerRequest request, string cleaned)
    {
        var files = SelectRelevantFiles(request.Retrieval.Results);
        var body = BuildCompactPromptBody(cleaned);
        return new PromptCompilerResult
        {
            OptimizedPrompt = TokenEfficientPromptAssembler.Assemble(body, request, request.TargetAgent),
            RelevantFiles = files,
            Tasks = new List<string> { ToImperative(cleaned) },
            Constraints = new List<string>
            {
                "Only edit listed files.",
                "Avoid unrelated refactors."
            },
            TokenSavingReason = "Focused the request on retrieved project files to reduce repo-wide searching.",
            RetrievalSummary = $"{request.Retrieval.RetrievalMode}: {files.Count} files"
        };
    }

    private const int MaxWriterChunks = 5;
    private const int MaxChunkChars = 700;

    private const string WriterInstruction =
        "You are Woody, a token-saving prompt compiler for Codex, Cursor, and Claude Code. " +
        "Your job is to rewrite the user request in FEWER words with the SAME meaning.\n" +
        "Hard rules:\n" +
        "- optimizedPrompt must be 1-3 short sentences, max 45 words total.\n" +
        "- No headers (no Goal/Context/Steps). No file paths. No bullet lists.\n" +
        "- Tell the agent what to do, not how to explore the whole repo.\n" +
        "- relevantFiles can be empty; Woody adds exact file line regions separately.\n" +
        "- Preserve every concrete detail from the user (names like BOS, feature names, UI areas).\n" +
        "Return strict JSON with keys: optimizedPrompt, relevantFiles, tasks, constraints, tokenSavingReason, warnings.";

    private const string WriterExample =
        "Example response:\n" +
        "{\"optimizedPrompt\":\"Find where BOS is configured in the app, including UI and settings. Report the exact files and what each controls.\"," +
        "\"relevantFiles\":[],\"tasks\":[\"Locate BOS configuration\"],\"constraints\":[\"Do not refactor unrelated code\"]," +
        "\"tokenSavingReason\":\"Short intent; files attached with line numbers separately\",\"warnings\":[]}";

    // Feeds Gemma a tight instruction, a worked example, and real code for the few most relevant chunks.
    private static string BuildWriterPrompt(PromptCompilerRequest request, string cleaned)
    {
        var stack = request.Brain?.Stack.Describe().ToList() ?? new List<string>();
        var chunks = request.Retrieval.Results.Take(MaxWriterChunks).Select(result => new
        {
            file = result.FilePath,
            symbol = string.IsNullOrWhiteSpace(result.Symbol) ? "none" : result.Symbol,
            lines = $"{result.StartLine}-{result.EndLine}",
            why = result.Reason,
            code = TrimCode(string.IsNullOrWhiteSpace(result.Content) ? result.ContentPreview : result.Content)
        });

        var payload = JsonSerializer.Serialize(new
        {
            userPrompt = cleaned,
            targetAgent = request.TargetAgent.ToString(),
            projectStack = stack,
            retrievalMode = request.Retrieval.RetrievalMode.ToString(),
            missingContext = request.MissingContext,
            retrievedContext = chunks
        }, BrainJson.Options);

        return $"{WriterInstruction}\n\n{WriterExample}\n\nNow compile this:\n{payload}";
    }

    private static string TrimCode(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var normalized = content.Replace("\r\n", "\n").Trim();
        return normalized.Length <= MaxChunkChars ? normalized : normalized[..MaxChunkChars] + "\n...";
    }

    private static PromptCompilerResult? TryParseStructured(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var trimmed = response.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            trimmed = trimmed[start..(end + 1)];
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            return new PromptCompilerResult
            {
                OptimizedPrompt = ReadString(root, "optimizedPrompt"),
                RelevantFiles = ReadStringArray(root, "relevantFiles"),
                Tasks = ReadStringArray(root, "tasks"),
                Constraints = ReadStringArray(root, "constraints"),
                TokenSavingReason = ReadString(root, "tokenSavingReason"),
                Warnings = ReadStringArray(root, "warnings")
            };
        }
        catch
        {
            return new PromptCompilerResult
            {
                OptimizedPrompt = response.Trim(),
                TokenSavingReason = "Writer returned plain text.",
                Warnings = new List<string>()
            };
        }
    }

    private static List<string> FilterRealFiles(IEnumerable<string> files, IReadOnlyList<RetrievalResult> results)
    {
        var real = results.Select(result => result.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return files.Where(file => real.Contains(file)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string BuildShorter(PromptCompilerResult result)
    {
        var task = result.Tasks.FirstOrDefault() ?? "Improve the request with minimal changes.";
        return task;
    }

    private static List<string> SelectRelevantFiles(IReadOnlyList<RetrievalResult> results) =>
        results
            .Where(result => !string.IsNullOrWhiteSpace(result.FilePath))
            .OrderByDescending(result => result.Score)
            .Select(result => result.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRelevantFiles)
            .ToList();

    private static string BuildCompactPromptBody(string cleaned)
    {
        var task = ToImperative(cleaned);
        return $"{task} Read only the listed file regions below. Keep changes minimal.";
    }

    private static string CompactGemmaBody(string body, string original)
    {
        var words = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 45)
        {
            return body.Trim();
        }

        return string.Join(' ', words.Take(45)).Trim() + ".";
    }

    private static string StripEmbeddedFileSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var markers = new[]
        {
            "\nFiles:",
            "\nFile:",
            "\nRelevant files:",
            "\nNecessary files:",
            "\nNecessary files for this task:",
            "\nRead at files:"
        };

        var cut = text.Length;
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        return cut < text.Length ? text[..cut].Trim() : text.Trim();
    }

    private static string StripWriterArtifacts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var markers = new[]
        {
            "\nReturn strict JSON",
            "\r\nReturn strict JSON",
            "\n{\"optimizedPrompt\"",
            "\r\n{\"optimizedPrompt\""
        };

        var cut = text.Length;
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index < cut)
            {
                cut = index;
            }
        }

        return cut < text.Length ? text[..cut].Trim() : text.Trim();
    }

    private static string CleanPrompt(string prompt)
    {
        return string.Join(' ', prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string ToImperative(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Improve the current prompt with minimal changes.";
        }

        var text = char.ToUpperInvariant(prompt[0]) + prompt[1..];
        return text.EndsWith('.') ? text : text + ".";
    }

    private static int EstimateTokens(string text) => string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (int)Math.Round(text.Length / 4.0));

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static List<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }
}
