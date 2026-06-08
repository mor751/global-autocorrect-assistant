using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Autocorrect.Core.Brain;

public sealed class VectorStoreStats
{
    public bool IsAvailable { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    public int VectorDimension { get; set; }
    public long VectorCount { get; set; }
    public string? Error { get; set; }
}

public interface IProjectVectorStore : IDisposable
{
    Task<VectorStoreStats> GetStatsAsync(string collectionName, CancellationToken cancellationToken);

    Task<bool> EnsureCollectionAsync(string collectionName, int vectorDimension, CancellationToken cancellationToken);

    Task UpsertAsync(string collectionName, IReadOnlyList<ProjectChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetrievalResult>> SearchAsync(string collectionName, float[] queryVector, int topK, CancellationToken cancellationToken);

    Task DeleteFileAsync(string collectionName, string filePath, CancellationToken cancellationToken);

    Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken);
}

public sealed class QdrantVectorStore : IProjectVectorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public QdrantVectorStore(string endpoint)
    {
        var normalized = string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:6333" : endpoint.TrimEnd('/');
        _httpClient = new HttpClient { BaseAddress = new Uri(normalized), Timeout = TimeSpan.FromSeconds(10) };
        _ownsClient = true;
    }

    internal QdrantVectorStore(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<VectorStoreStats> GetStatsAsync(string collectionName, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"/collections/{collectionName}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new VectorStoreStats { CollectionName = collectionName, IsAvailable = true, Error = "collection_missing" };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new VectorStoreStats
                {
                    CollectionName = collectionName,
                    IsAvailable = false,
                    Error = $"Qdrant HTTP {(int)response.StatusCode}"
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var result = document.RootElement.GetProperty("result");
            return new VectorStoreStats
            {
                CollectionName = collectionName,
                IsAvailable = true,
                VectorCount = ReadLong(result, "points_count", "vectors_count"),
                VectorDimension = ReadVectorSize(result)
            };
        }
        catch (Exception ex)
        {
            return new VectorStoreStats
            {
                CollectionName = collectionName,
                IsAvailable = false,
                Error = ex.Message
            };
        }
    }

    public async Task<bool> EnsureCollectionAsync(string collectionName, int vectorDimension, CancellationToken cancellationToken)
    {
        var stats = await GetStatsAsync(collectionName, cancellationToken);
        if (!stats.IsAvailable)
        {
            return false;
        }

        if (stats.VectorDimension == vectorDimension && stats.VectorDimension > 0)
        {
            return true;
        }

        if (stats.VectorDimension > 0 && stats.VectorDimension != vectorDimension)
        {
            await ClearCollectionAsync(collectionName, cancellationToken);
        }

        var payload = new
        {
            vectors = new
            {
                size = vectorDimension,
                distance = "Cosine"
            }
        };

        using var response = await _httpClient.PutAsJsonAsync($"/collections/{collectionName}", payload, JsonOptions, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task UpsertAsync(string collectionName, IReadOnlyList<ProjectChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0 || vectors.Count == 0)
        {
            return;
        }

        var points = chunks.Zip(vectors).Select(pair => new
        {
            id = pair.First.Id,
            vector = pair.Second,
            payload = ToPayload(pair.First)
        }).ToList();

        foreach (var batch in points.Chunk(64))
        {
            var body = new { points = batch };
            using var response = await _httpClient.PutAsJsonAsync($"/collections/{collectionName}/points?wait=true", body, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(string collectionName, float[] queryVector, int topK, CancellationToken cancellationToken)
    {
        var body = new
        {
            vector = queryVector,
            limit = topK,
            with_payload = true
        };

        using var response = await _httpClient.PostAsJsonAsync($"/collections/{collectionName}/points/search", body, JsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<RetrievalResult>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RetrievalResult>();
        }

        return result.EnumerateArray()
            .Select(ParseSearchResult)
            .Where(r => !string.IsNullOrWhiteSpace(r.FilePath))
            .ToList();
    }

    public async Task DeleteFileAsync(string collectionName, string filePath, CancellationToken cancellationToken)
    {
        var body = new
        {
            filter = new
            {
                must = new[]
                {
                    new
                    {
                        key = "filePath",
                        match = new { value = filePath }
                    }
                }
            }
        };

        using var response = await _httpClient.PostAsJsonAsync($"/collections/{collectionName}/points/delete?wait=true", body, JsonOptions, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync($"/collections/{collectionName}", cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    private static Dictionary<string, object?> ToPayload(ProjectChunk chunk) => new()
    {
        ["projectId"] = chunk.ProjectId,
        ["projectRoot"] = chunk.ProjectRoot,
        ["filePath"] = chunk.FilePath,
        ["fileName"] = chunk.FileName,
        ["folder"] = chunk.Folder,
        ["extension"] = chunk.Extension,
        ["language"] = chunk.Language,
        ["chunkType"] = chunk.ChunkType,
        ["symbol"] = chunk.Symbol,
        ["parentSymbol"] = chunk.ParentSymbol,
        ["startLine"] = chunk.StartLine,
        ["endLine"] = chunk.EndLine,
        ["content"] = chunk.Content,
        ["contentPreview"] = chunk.ContentPreview,
        ["metadataJson"] = chunk.MetadataJson,
        ["imports"] = chunk.Imports,
        ["exports"] = chunk.Exports,
        ["tags"] = chunk.Tags,
        ["fileHash"] = chunk.FileHash,
        ["chunkHash"] = chunk.ChunkHash,
        ["importance"] = chunk.Importance,
        ["indexedAt"] = chunk.IndexedAt
    };

    private static RetrievalResult ParseSearchResult(JsonElement element)
    {
        var payload = element.TryGetProperty("payload", out var p) ? p : default;
        var score = element.TryGetProperty("score", out var scoreElement) ? scoreElement.GetDouble() : 0;
        return new RetrievalResult
        {
            Score = score,
            FilePath = ReadString(payload, "filePath"),
            ChunkType = ReadString(payload, "chunkType"),
            Symbol = ReadString(payload, "symbol"),
            StartLine = ReadInt(payload, "startLine"),
            EndLine = ReadInt(payload, "endLine"),
            Reason = "semantic match",
            ContentPreview = ReadString(payload, "contentPreview"),
            Content = ReadString(payload, "content"),
            Metadata = PayloadMetadata(payload)
        };
    }

    private static Dictionary<string, string> PayloadMetadata(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in payload.EnumerateObject())
        {
            metadata[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => property.Value.GetRawText()
            };
        }

        return metadata;
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt64();
            }
        }

        return 0;
    }

    private static int ReadVectorSize(JsonElement result)
    {
        try
        {
            var vectors = result.GetProperty("config").GetProperty("params").GetProperty("vectors");
            if (vectors.TryGetProperty("size", out var size))
            {
                return size.GetInt32();
            }
        }
        catch
        {
            // Shape can vary slightly between Qdrant versions.
        }

        return 0;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
