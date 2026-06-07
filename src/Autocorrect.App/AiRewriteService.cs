using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class CompositeAiRewriteService : IAiRewriteService, IDisposable
{
    private readonly LocalRewriteService _local = new();
    private readonly OpenAiCompatibleRewriteService _remote = new();

    public bool IsEnabled(CorrectionSettings settings)
    {
        return settings.AiOverlayEnabled && !settings.AiProvider.Equals("disabled", StringComparison.OrdinalIgnoreCase);
    }

    public Task<AiRewriteResult?> RewriteAsync(
        AiRewriteRequest request,
        CorrectionSettings settings,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(settings) || !request.AppContext.IsAllowedForAiOverlay || request.AppContext.IsSensitive)
        {
            return Task.FromResult<AiRewriteResult?>(null);
        }

        if (settings.LocalOnlyMode || settings.AiProvider.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<AiRewriteResult?>(_local.Rewrite(request));
        }

        return _remote.RewriteAsync(request, settings, cancellationToken);
    }

    public void Dispose()
    {
        _remote.Dispose();
    }
}

public sealed class LocalRewriteService
{
    public AiRewriteResult Rewrite(AiRewriteRequest request)
    {
        var fixedText = FixCommonTypos(request.Text);
        var rewritten = request.Action switch
        {
            AiRewriteAction.OptimizePrompt => OptimizePrompt(fixedText),
            AiRewriteAction.CompressTokens => Compress(fixedText),
            AiRewriteAction.MakeProfessional => MakeProfessional(fixedText),
            AiRewriteAction.MakeDirect => MakeDirect(fixedText),
            AiRewriteAction.VideoGenerationPrompt => OptimizeVideoPrompt(fixedText),
            AiRewriteAction.CursorCodingPrompt => OptimizeCodingPrompt(fixedText),
            _ => fixedText
        };

        var reduction = EstimateReduction(request.Text, rewritten);
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

    private static int EstimateReduction(string original, string rewritten)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Round((1 - rewritten.Length / (double)original.Length) * 100));
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

        var instruction = request.Action switch
        {
            AiRewriteAction.OptimizePrompt => "Optimize this as a clear, concise prompt.",
            AiRewriteAction.CompressTokens => "Compress this text while preserving meaning.",
            AiRewriteAction.MakeProfessional => "Rewrite professionally.",
            AiRewriteAction.MakeDirect => "Rewrite directly and concisely.",
            _ => "Fix typos and improve clarity."
        };

        var payload = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
                new { role = "system", content = instruction },
                new { role = "user", content = request.Text }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(settings.AiEndpoint, payload, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return new AiRewriteResult(json, "OpenAI-compatible provider response.", 0, 0.7);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
