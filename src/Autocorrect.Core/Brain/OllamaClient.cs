using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Autocorrect.Core.Brain;

public sealed record OllamaSettings(string Endpoint, string ChatModel, string EmbeddingModel)
{
    public static OllamaSettings Default { get; } = new("http://localhost:11434", "gemma3:4b", "nomic-embed-text");
}

// Generation knobs; NumPredict <= 0 means "let the model decide" (no cap sent).
public sealed record OllamaGenerateOptions(double Temperature = 0.2, int NumPredict = 0)
{
    public static OllamaGenerateOptions Default { get; } = new();
}

public interface IOllamaClient
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken);

    Task<string?> GenerateAsync(string prompt, CancellationToken cancellationToken);

    Task<string?> GenerateAsync(string prompt, OllamaGenerateOptions options, CancellationToken cancellationToken);

    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken);
}

// Talks to a local Ollama server; every call fails soft (null/false) so the app keeps working offline.
public sealed class OllamaClient : IOllamaClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly OllamaSettings _settings;

    public OllamaClient(OllamaSettings settings)
    {
        _settings = settings;
    }

    private string Endpoint => (string.IsNullOrWhiteSpace(_settings.Endpoint) ? OllamaSettings.Default.Endpoint : _settings.Endpoint).TrimEnd('/');

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            using var response = await _httpClient.GetAsync($"{Endpoint}/api/tags", linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{Endpoint}/api/tags", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("models", out var models))
            {
                return Array.Empty<string>();
            }

            return models.EnumerateArray()
                .Select(m => m.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty)
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public Task<string?> GenerateAsync(string prompt, CancellationToken cancellationToken) =>
        GenerateAsync(prompt, OllamaGenerateOptions.Default, cancellationToken);

    public async Task<string?> GenerateAsync(string prompt, OllamaGenerateOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        object generationOptions = options.NumPredict > 0
            ? new { temperature = options.Temperature, num_predict = options.NumPredict }
            : new { temperature = options.Temperature };

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_settings.ChatModel) ? OllamaSettings.Default.ChatModel : _settings.ChatModel,
            prompt,
            stream = false,
            options = generationOptions
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"{Endpoint}/api/generate", payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("response", out var content) ? content.GetString()?.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(_settings.EmbeddingModel) ? OllamaSettings.Default.EmbeddingModel : _settings.EmbeddingModel,
            prompt = text
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"{Endpoint}/api/embeddings", payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("embedding", out var embedding) || embedding.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var vector = embedding.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            return vector.Length > 0 ? vector : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
