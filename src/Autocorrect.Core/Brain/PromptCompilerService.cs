using System.Text;
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
        var response = await _writer.GenerateAsync(writerPrompt, new OllamaGenerateOptions(0.15, 450), cancellationToken);
        var parsed = TryParseStructured(response);
        if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.OptimizedPrompt))
        {
            parsed.UsedWriterModel = true;
            parsed.RelevantFiles = FilterRealFiles(parsed.RelevantFiles, request.Retrieval.Results);
            if (parsed.RelevantFiles.Count == 0)
            {
                parsed.RelevantFiles = deterministic.RelevantFiles;
            }

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

    private static PromptCompilerResult BuildDeterministic(PromptCompilerRequest request, string cleaned)
    {
        var files = request.Retrieval.Results
            .Select(result => result.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        var stack = request.Brain?.Stack.Describe().ToList() ?? new List<string>();
        var task = ToImperative(cleaned);

        var builder = new StringBuilder();
        builder.AppendLine($"Task: {task}");
        if (stack.Count > 0)
        {
            builder.AppendLine($"Stack: {string.Join(", ", stack)}");
        }

        if (files.Count > 0)
        {
            builder.AppendLine("Files:");
            foreach (var file in files)
            {
                builder.AppendLine($"- {file}");
            }
        }
        else if (request.MissingContext.Count > 0)
        {
            builder.AppendLine("Missing context (add before sending):");
            foreach (var missing in request.MissingContext.Take(3))
            {
                builder.AppendLine($"- {missing}");
            }
        }

        builder.AppendLine("Do:");
        builder.AppendLine("- Inspect the listed files first, then make the smallest change that satisfies the task.");
        builder.AppendLine("Avoid:");
        builder.AppendLine("- Refactoring unrelated files, routes, or tests.");

        return new PromptCompilerResult
        {
            OptimizedPrompt = AgentAdapt(builder.ToString().Trim(), request.TargetAgent),
            RelevantFiles = files,
            Tasks = new List<string> { task },
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
        "You are Woody, a Project-Aware Prompt Compiler for coding agents (Cursor, Codex, Claude Code). " +
        "Rewrite the user's messy request into ONE precise, token-efficient prompt the agent can act on immediately.\n" +
        "Hard rules:\n" +
        "- optimizedPrompt must be <= 150 words. No filler, no preamble, do not repeat these rules.\n" +
        "- Only use file paths found in retrievedContext. Never invent paths.\n" +
        "- Reference only the 1-3 most relevant files; drop weakly related ones.\n" +
        "- Keep the user's original intent and every concrete detail.\n" +
        "- Tell the agent to inspect those files first and not refactor unrelated code.\n" +
        "- If missingContext is non-empty or the request is too vague to act on, make optimizedPrompt a short clarifying question listing what is missing, and copy those items into warnings.\n" +
        "optimizedPrompt must follow this skeleton, omitting empty sections:\n" +
        "Task: <one sentence>\n" +
        "Files: <path - why it matters>\n" +
        "Do: <2-4 short imperative steps>\n" +
        "Avoid: <1-2 short limits>\n" +
        "Return strict JSON with keys: optimizedPrompt, relevantFiles, tasks, constraints, tokenSavingReason, warnings.";

    private const string WriterExample =
        "Example response:\n" +
        "{\"optimizedPrompt\":\"Task: Fix the login form entry animation.\\nFiles: src/components/LoginForm.tsx - animation is defined here\\nDo:\\n- Inspect LoginForm.tsx first\\n- Adjust the entry animation timing/easing\\nAvoid:\\n- Changing auth logic or unrelated files\"," +
        "\"relevantFiles\":[\"src/components/LoginForm.tsx\"],\"tasks\":[\"Fix login form animation\"],\"constraints\":[\"Do not touch auth logic\"],\"tokenSavingReason\":\"Pointed the agent to one real file\",\"warnings\":[]}";

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

    private static string AgentAdapt(string prompt, PromptTargetAgent agent)
    {
        return agent switch
        {
            PromptTargetAgent.Cursor => prompt + "\n\nCursor: inspect the listed files before editing and keep the change minimal.",
            PromptTargetAgent.ClaudeCode => prompt + "\n\nClaude Code: summarize your plan before editing.",
            PromptTargetAgent.Codex => prompt + "\n\nCodex: keep the change scoped and include verification steps.",
            _ => prompt
        };
    }

    private static string BuildShorter(PromptCompilerResult result)
    {
        var files = result.RelevantFiles.Count == 0 ? string.Empty : $" Files: {string.Join("; ", result.RelevantFiles.Take(5))}.";
        var task = result.Tasks.FirstOrDefault() ?? "Improve the request with minimal changes.";
        return $"{task}{files} Keep scope tight; avoid unrelated refactors.";
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
