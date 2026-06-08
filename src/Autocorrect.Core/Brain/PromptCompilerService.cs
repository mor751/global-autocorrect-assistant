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
        var response = await _writer.GenerateAsync(writerPrompt, cancellationToken);
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
            .Take(8)
            .ToList();
        var stack = request.Brain?.Stack.Describe().ToList() ?? new List<string>();
        var task = ToImperative(cleaned);

        var builder = new StringBuilder();
        builder.AppendLine(task).AppendLine();
        builder.AppendLine("Project context:");
        builder.AppendLine(stack.Count > 0
            ? $"This appears to be a {string.Join(" + ", stack)} project."
            : "Project stack is unknown.");
        builder.AppendLine();

        if (files.Count > 0)
        {
            builder.AppendLine("Inspect these files first:");
            foreach (var file in files)
            {
                builder.AppendLine($"- {file}");
            }
        }
        else
        {
            builder.AppendLine("No exact relevant files found. Start by searching for:");
            builder.AppendLine($"- {cleaned}");
        }

        builder.AppendLine();
        builder.AppendLine("Tasks:");
        builder.AppendLine("1. Identify the smallest relevant change.");
        builder.AppendLine("2. Preserve existing behavior unless the prompt explicitly asks to change it.");
        builder.AppendLine("3. Improve only the files directly related to this request.");
        builder.AppendLine("4. Do not refactor unrelated UI, routes, services, or tests.");
        builder.AppendLine("5. Before editing, explain the likely cause and the minimal files you plan to change.");

        return new PromptCompilerResult
        {
            OptimizedPrompt = AgentAdapt(builder.ToString().Trim(), request.TargetAgent),
            RelevantFiles = files,
            Tasks = new List<string> { task },
            Constraints = new List<string>
            {
                "Only mention real files from retrieval.",
                "Avoid unrelated refactors.",
                "Inspect relevant files before editing."
            },
            TokenSavingReason = "Focused the request on retrieved project files to reduce repo-wide searching.",
            RetrievalSummary = $"{request.Retrieval.RetrievalMode}: {files.Count} files"
        };
    }

    private static string BuildWriterPrompt(PromptCompilerRequest request, string cleaned)
    {
        var stack = request.Brain?.Stack.Describe().ToList();
        var chunks = request.Retrieval.Results.Take(8).Select(result => new
        {
            result.FilePath,
            result.ChunkType,
            result.Symbol,
            result.StartLine,
            result.EndLine,
            result.Reason,
            Preview = result.ContentPreview
        });

        var payload = JsonSerializer.Serialize(new
        {
            userPrompt = cleaned,
            targetAgent = request.TargetAgent.ToString(),
            projectStack = stack,
            retrievalMode = request.Retrieval.RetrievalMode.ToString(),
            retrievedContext = chunks
        }, BrainJson.Options);

        return "You are Woody, a local Project-Aware Prompt Compiler for coding agents. " +
               "Rewrite the user's messy request into a precise, token-efficient prompt. " +
               "Only mention file paths present in retrievedContext. Never invent paths. " +
               "Tell the coding agent to inspect relevant files first and avoid unrelated refactors. " +
               "Return strict JSON with keys: optimizedPrompt, relevantFiles, tasks, constraints, tokenSavingReason, warnings.\n\n" +
               payload;
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
