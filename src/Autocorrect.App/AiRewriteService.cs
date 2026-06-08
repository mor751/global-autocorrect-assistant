using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class CompositeAiRewriteService : IAiRewriteService, IDisposable
{
    private readonly LocalRewriteService _local = new();
    private readonly OllamaRewriteService _ollama = new();
    private readonly OpenAiCompatibleRewriteService _remote = new();

    // On-demand rewriting is available whenever a provider other than "disabled" is selected.
    public bool IsEnabled(CorrectionSettings settings)
    {
        return !settings.AiProvider.Equals("disabled", StringComparison.OrdinalIgnoreCase);
    }

    public Task<AiRewriteResult?> RewriteAsync(
        AiRewriteRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(settings) || request.AppContext.IsSensitive)
        {
            return Task.FromResult<AiRewriteResult?>(null);
        }

        return settings.AiProvider.ToLowerInvariant() switch
        {
            "ollama" => _ollama.RewriteAsync(request, settings, cancellationToken),
            "openai" or "remote" when !settings.LocalOnlyMode => _remote.RewriteAsync(request, settings, cancellationToken),
            _ => Task.FromResult<AiRewriteResult?>(_local.Rewrite(request))
        };
    }

    public void Dispose()
    {
        _ollama.Dispose();
        _remote.Dispose();
    }
}

// Shared, model-agnostic instructions and token math for any LLM-backed rewrite provider.
public static class RewriteInstructions
{
    public static string For(AiRewriteAction action)
    {
        return action switch
        {
            AiRewriteAction.FixTyposOnly => "Fix only spelling and grammar. Keep wording, meaning, and style unchanged.",
            AiRewriteAction.SmartOptimize => "Fix all spelling and grammar, sharpen the wording, and compress the text into a clear, token-efficient prompt for an AI assistant. Preserve the exact intent and every concrete detail while removing filler and redundancy.",
            AiRewriteAction.ImproveClarity => "Rewrite the text to be clearer and easier to read while preserving all meaning and details.",
            AiRewriteAction.OptimizePrompt => "Rewrite this into a clear, well-structured, token-efficient prompt for an AI assistant. Preserve the intent and every concrete detail, but remove filler and redundancy so it uses fewer tokens.",
            AiRewriteAction.CompressTokens => "Rewrite this prompt to use as few tokens as possible while preserving the exact meaning, intent, and all concrete details. Remove filler and redundancy.",
            AiRewriteAction.MakeProfessional => "Rewrite the text in a professional tone while preserving meaning.",
            AiRewriteAction.MakeDirect => "Rewrite the text to be direct and concise while preserving meaning.",
            AiRewriteAction.CursorCodingPrompt => "Rewrite this into a precise coding instruction for an AI code assistant: state the goal, constraints, and expected change clearly.",
            AiRewriteAction.VideoGenerationPrompt => "Rewrite this into a vivid, concise video-generation prompt describing subject, action, style, and camera.",
            _ => "Improve this text while preserving meaning."
        };
    }

    public static string SystemPrompt(AiRewriteAction action)
    {
        return For(action) + " Output ONLY the rewritten text with no preamble, quotes, or explanation.";
    }

    // Rough token estimate (~4 chars/token) used to report savings without a tokenizer dependency.
    public static int ReductionPercent(string original, string rewritten)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return 0;
        }

        var before = Math.Max(1, original.Length);
        return Math.Max(0, (int)Math.Round((1 - rewritten.Length / (double)before) * 100));
    }
}

public sealed class OllamaRewriteService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<AiRewriteResult?> RewriteAsync(
        AiRewriteRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return null;
        }

        var endpoint = (string.IsNullOrWhiteSpace(settings.AiEndpoint) ? "http://localhost:11434" : settings.AiEndpoint).TrimEnd('/');
        var model = string.IsNullOrWhiteSpace(settings.AiModel) ? "qwen2.5:3b" : settings.AiModel;
        var payload = new
        {
            model,
            stream = false,
            options = new { temperature = 0.2 },
            messages = new[]
            {
                new { role = "system", content = RewriteInstructions.SystemPrompt(request.Action) },
                new { role = "user", content = request.Text }
            }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"{endpoint}/api/chat", payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
            {
                return null;
            }

            var rewritten = CleanOutput(content.GetString());
            if (string.IsNullOrWhiteSpace(rewritten))
            {
                return null;
            }

            var reduction = RewriteInstructions.ReductionPercent(request.Text, rewritten);
            return new AiRewriteResult(rewritten, $"Local Ollama model ({model}). Nothing left your machine.", reduction, 0.85);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Strips wrapping quotes/whitespace that small models sometimes add around the answer.
    private static string CleanOutput(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

public sealed class LocalRewriteService
{
    public AiRewriteResult Rewrite(AiRewriteRequest request)
    {
        var fixedText = FixCommonTypos(request.Text);
        var rewritten = request.Action switch
        {
            AiRewriteAction.SmartOptimize => Compress(OptimizePrompt(fixedText)),
            AiRewriteAction.OptimizePrompt => OptimizePrompt(fixedText),
            AiRewriteAction.CompressTokens => Compress(fixedText),
            AiRewriteAction.MakeProfessional => MakeProfessional(fixedText),
            AiRewriteAction.MakeDirect => MakeDirect(fixedText),
            AiRewriteAction.VideoGenerationPrompt => OptimizeVideoPrompt(fixedText),
            AiRewriteAction.CursorCodingPrompt => OptimizeCodingPrompt(fixedText),
            _ => fixedText
        };

        var reduction = RewriteInstructions.ReductionPercent(request.Text, rewritten);
        return new AiRewriteResult(rewritten, "Local rewrite. No text was sent to an external service.", reduction, 0.78);
    }

    private static string FixCommonTypos(string text)
    {
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["vidoe"] = "video",
            ["viede"] = "video",
            ["chercter"] = "character",
            ["stlye"] = "style",
            ["promto"] = "prompt",
            ["protm"] = "prompt",
            ["reqeite"] = "rewrite",
            ["tokesn"] = "tokens",
            ["maek"] = "make",
            ["iamge"] = "image"
        };

        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var trimmed = words[i].Trim(',', '.', '!', '?', ';', ':');
            if (replacements.TryGetValue(trimmed, out var replacement))
            {
                words[i] = words[i].Replace(trimmed, replacement, StringComparison.OrdinalIgnoreCase);
            }
        }

        return string.Join(' ', words).Trim();
    }

    private static string OptimizePrompt(string text)
    {
        var cleaned = NormalizeSentence(text);
        return cleaned.Length == 0 ? text : cleaned;
    }

    private static string OptimizeVideoPrompt(string text)
    {
        var cleaned = NormalizeSentence(text);
        cleaned = cleaned.Replace(" no text", ". No on-screen text", StringComparison.OrdinalIgnoreCase);
        cleaned = cleaned.Replace(" same character", "Keep the same reference character", StringComparison.OrdinalIgnoreCase);
        return cleaned;
    }

    private static string OptimizeCodingPrompt(string text)
    {
        return $"Implement this carefully. Goal: {NormalizeSentence(text)} Keep the change modular, tested, and avoid unrelated refactors.";
    }

    private static string Compress(string text)
    {
        var cleaned = NormalizeSentence(text);
        var remove = new[] { "please ", "i need ", "can you ", "make sure ", "very ", "really " };
        foreach (var token in remove)
        {
            cleaned = cleaned.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return cleaned;
    }

    private static string MakeProfessional(string text)
    {
        return NormalizeSentence(text).Replace(" bro", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string MakeDirect(string text)
    {
        return Compress(text);
    }

    private static string NormalizeSentence(string text)
    {
        var cleaned = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (cleaned.Length == 0)
        {
            return cleaned;
        }

        cleaned = char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
        return cleaned.EndsWith('.') || cleaned.EndsWith('!') || cleaned.EndsWith('?') ? cleaned : cleaned + ".";
    }
}

public sealed class OpenAiCompatibleRewriteService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public async Task<AiRewriteResult?> RewriteAsync(
        AiRewriteRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.AiEndpoint))
        {
            return null;
        }

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(settings.AiModel) ? "gpt-4o-mini" : settings.AiModel,
            messages = new[]
            {
                new { role = "system", content = RewriteInstructions.SystemPrompt(request.Action) },
                new { role = "user", content = request.Text }
            }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(settings.AiEndpoint, payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("message", out var message) ||
                !message.TryGetProperty("content", out var content))
            {
                return null;
            }

            var rewritten = (content.GetString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(rewritten))
            {
                return null;
            }

            return new AiRewriteResult(rewritten, "Cloud provider response.", RewriteInstructions.ReductionPercent(request.Text, rewritten), 0.8);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
