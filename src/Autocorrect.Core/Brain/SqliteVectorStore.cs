using System.IO;
using Microsoft.Data.Sqlite;

namespace Autocorrect.Core.Brain;

public sealed class VectorStoreStats
{
    public bool IsAvailable { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    public int VectorDimension { get; set; }
    public long VectorCount { get; set; }
    public string? Error { get; set; }
}

// A single stored vector plus the metadata needed to visualize and label it.
public sealed class VectorPoint
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string ChunkType { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public float[] Vector { get; set; } = Array.Empty<float>();
}

public interface IProjectVectorStore : IDisposable
{
    Task<VectorStoreStats> GetStatsAsync(string collectionName, CancellationToken cancellationToken);

    Task<bool> EnsureCollectionAsync(string collectionName, int vectorDimension, CancellationToken cancellationToken);

    Task UpsertAsync(string collectionName, IReadOnlyList<ProjectChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetrievalResult>> SearchAsync(string collectionName, float[] queryVector, int topK, CancellationToken cancellationToken);

    Task<IReadOnlyList<VectorPoint>> ExportAsync(string collectionName, CancellationToken cancellationToken);

    Task DeleteFileAsync(string collectionName, string filePath, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetrievalResult>> GetChunksByFilesAsync(string collectionName, IReadOnlyList<string> filePaths, int perFile, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetrievalResult>> SearchBySymbolsAsync(string collectionName, IReadOnlyList<string> symbols, int perSymbol, CancellationToken cancellationToken);

    Task<IReadOnlyList<RetrievalResult>> SearchByTermsAsync(string collectionName, IReadOnlyList<string> terms, int perTerm, CancellationToken cancellationToken);

    Task UpsertSymbolGraphAsync(string collectionName, ProjectGraph graph, CancellationToken cancellationToken);

    Task<(int Nodes, int Edges)> GetSymbolGraphStatsAsync(string collectionName, CancellationToken cancellationToken);

    Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken);
}

// Embedded, on-disk vector DB (SQLite). Ships inside the installer, no server/Docker; exact cosine search.
public sealed class SqliteVectorStore : IProjectVectorStore
{
    private readonly SqliteConnection _connection;

    public SqliteVectorStore(string baseDirectory)
    {
        var directory = Path.Combine(baseDirectory, "vectors");
        Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "woody-vectors.db");
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        InitializeSchema();
    }

    public Task<VectorStoreStats> GetStatsAsync(string collectionName, CancellationToken cancellationToken)
    {
        var stats = new VectorStoreStats { CollectionName = collectionName, IsAvailable = true };
        try
        {
            using var count = _connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM chunks WHERE collection = $c";
            count.Parameters.AddWithValue("$c", collectionName);
            stats.VectorCount = Convert.ToInt64(count.ExecuteScalar() ?? 0L);
            stats.VectorDimension = ReadDimension(collectionName);
        }
        catch (Exception ex)
        {
            stats.IsAvailable = false;
            stats.Error = ex.Message;
        }

        return Task.FromResult(stats);
    }

    public Task<bool> EnsureCollectionAsync(string collectionName, int vectorDimension, CancellationToken cancellationToken)
    {
        var existing = ReadDimension(collectionName);
        if (existing > 0 && existing != vectorDimension)
        {
            ClearCollection(collectionName);
        }

        using var meta = _connection.CreateCommand();
        meta.CommandText = "INSERT INTO collection_meta (collection, dim) VALUES ($c, $d) " +
                           "ON CONFLICT(collection) DO UPDATE SET dim = excluded.dim";
        meta.Parameters.AddWithValue("$c", collectionName);
        meta.Parameters.AddWithValue("$d", vectorDimension);
        meta.ExecuteNonQuery();
        return Task.FromResult(true);
    }

    public Task UpsertAsync(string collectionName, IReadOnlyList<ProjectChunk> chunks, IReadOnlyList<float[]> vectors, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0 || vectors.Count == 0)
        {
            return Task.CompletedTask;
        }

        using var transaction = _connection.BeginTransaction();
        using var command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO chunks (id, collection, filePath, fileName, folder, extension, language, chunkType, symbol, startLine, endLine, content, contentPreview, importance, fileHash, chunkHash, vector) " +
            "VALUES ($id, $c, $fp, $fn, $fo, $ext, $lang, $ct, $sym, $sl, $el, $content, $preview, $imp, $fh, $ch, $vec) " +
            "ON CONFLICT(id) DO UPDATE SET collection=excluded.collection, filePath=excluded.filePath, fileName=excluded.fileName, folder=excluded.folder, extension=excluded.extension, language=excluded.language, chunkType=excluded.chunkType, symbol=excluded.symbol, startLine=excluded.startLine, endLine=excluded.endLine, content=excluded.content, contentPreview=excluded.contentPreview, importance=excluded.importance, fileHash=excluded.fileHash, chunkHash=excluded.chunkHash, vector=excluded.vector";

        var pId = command.Parameters.Add("$id", SqliteType.Text);
        var pCollection = command.Parameters.Add("$c", SqliteType.Text);
        var pFilePath = command.Parameters.Add("$fp", SqliteType.Text);
        var pFileName = command.Parameters.Add("$fn", SqliteType.Text);
        var pFolder = command.Parameters.Add("$fo", SqliteType.Text);
        var pExtension = command.Parameters.Add("$ext", SqliteType.Text);
        var pLanguage = command.Parameters.Add("$lang", SqliteType.Text);
        var pChunkType = command.Parameters.Add("$ct", SqliteType.Text);
        var pSymbol = command.Parameters.Add("$sym", SqliteType.Text);
        var pStartLine = command.Parameters.Add("$sl", SqliteType.Integer);
        var pEndLine = command.Parameters.Add("$el", SqliteType.Integer);
        var pContent = command.Parameters.Add("$content", SqliteType.Text);
        var pPreview = command.Parameters.Add("$preview", SqliteType.Text);
        var pImportance = command.Parameters.Add("$imp", SqliteType.Real);
        var pFileHash = command.Parameters.Add("$fh", SqliteType.Text);
        var pChunkHash = command.Parameters.Add("$ch", SqliteType.Text);
        var pVector = command.Parameters.Add("$vec", SqliteType.Blob);

        var count = Math.Min(chunks.Count, vectors.Count);
        for (var i = 0; i < count; i++)
        {
            var chunk = chunks[i];
            pId.Value = chunk.Id;
            pCollection.Value = collectionName;
            pFilePath.Value = chunk.FilePath;
            pFileName.Value = chunk.FileName;
            pFolder.Value = chunk.Folder;
            pExtension.Value = chunk.Extension;
            pLanguage.Value = chunk.Language;
            pChunkType.Value = chunk.ChunkType;
            pSymbol.Value = chunk.Symbol;
            pStartLine.Value = chunk.StartLine;
            pEndLine.Value = chunk.EndLine;
            pContent.Value = chunk.Content;
            pPreview.Value = chunk.ContentPreview;
            pImportance.Value = chunk.Importance;
            pFileHash.Value = chunk.FileHash;
            pChunkHash.Value = chunk.ChunkHash;
            pVector.Value = ToBlob(vectors[i]);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string collectionName, float[] queryVector, int topK, CancellationToken cancellationToken)
    {
        var results = new List<RetrievalResult>();
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT filePath, fileName, folder, extension, language, chunkType, symbol, startLine, endLine, content, contentPreview, importance, vector " +
            "FROM chunks WHERE collection = $c";
        command.Parameters.AddWithValue("$c", collectionName);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var blob = (byte[])reader["vector"];
            var vector = FromBlob(blob);
            var score = Cosine(queryVector, vector);
            if (score <= 0)
            {
                continue;
            }

            results.Add(new RetrievalResult
            {
                Score = score,
                FilePath = reader.GetString(0),
                ChunkType = reader.GetString(5),
                Symbol = reader.GetString(6),
                StartLine = reader.GetInt32(7),
                EndLine = reader.GetInt32(8),
                Reason = "semantic match",
                Content = reader.GetString(9),
                ContentPreview = reader.GetString(10),
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fileName"] = reader.GetString(1),
                    ["folder"] = reader.GetString(2),
                    ["extension"] = reader.GetString(3),
                    ["language"] = reader.GetString(4),
                    ["importance"] = reader.GetDouble(11).ToString("0.000")
                }
            });
        }

        var top = results
            .OrderByDescending(result => result.Score)
            .Take(Math.Max(1, topK))
            .ToList();
        return Task.FromResult<IReadOnlyList<RetrievalResult>>(top);
    }

    private static RetrievalResult ReadRetrievalResult(SqliteDataReader reader, double score, string reason)
    {
        return new RetrievalResult
        {
            Score = score,
            FilePath = reader.GetString(0),
            ChunkType = reader.GetString(5),
            Symbol = reader.GetString(6),
            StartLine = reader.GetInt32(7),
            EndLine = reader.GetInt32(8),
            Reason = reason,
            Content = reader.GetString(9),
            ContentPreview = reader.GetString(10),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["fileName"] = reader.GetString(1),
                ["folder"] = reader.GetString(2),
                ["extension"] = reader.GetString(3),
                ["language"] = reader.GetString(4),
                ["importance"] = reader.GetDouble(11).ToString("0.000")
            }
        };
    }

    public Task<IReadOnlyList<VectorPoint>> ExportAsync(string collectionName, CancellationToken cancellationToken)
    {
        var points = new List<VectorPoint>();
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT filePath, fileName, folder, extension, chunkType, symbol, startLine, endLine, vector " +
            "FROM chunks WHERE collection = $c";
        command.Parameters.AddWithValue("$c", collectionName);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new VectorPoint
            {
                FilePath = reader.GetString(0),
                FileName = reader.GetString(1),
                Folder = reader.GetString(2),
                Extension = reader.GetString(3),
                ChunkType = reader.GetString(4),
                Symbol = reader.GetString(5),
                StartLine = reader.GetInt32(6),
                EndLine = reader.GetInt32(7),
                Vector = FromBlob((byte[])reader["vector"])
            });
        }

        return Task.FromResult<IReadOnlyList<VectorPoint>>(points);
    }

    public Task<IReadOnlyList<RetrievalResult>> GetChunksByFilesAsync(string collectionName, IReadOnlyList<string> filePaths, int perFile, CancellationToken cancellationToken)
    {
        if (filePaths.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        var results = new List<RetrievalResult>();
        foreach (var filePath in filePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT filePath, fileName, folder, extension, language, chunkType, symbol, startLine, endLine, content, contentPreview, importance " +
                "FROM chunks WHERE collection = $c AND filePath = $fp ORDER BY importance DESC, startLine ASC LIMIT $limit";
            command.Parameters.AddWithValue("$c", collectionName);
            command.Parameters.AddWithValue("$fp", filePath);
            command.Parameters.AddWithValue("$limit", Math.Max(1, perFile));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadRetrievalResult(reader, 0.0, "graph neighbor"));
            }
        }

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(results);
    }

    public Task<IReadOnlyList<RetrievalResult>> SearchBySymbolsAsync(string collectionName, IReadOnlyList<string> symbols, int perSymbol, CancellationToken cancellationToken)
    {
        if (symbols.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        var results = new List<RetrievalResult>();
        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT filePath, fileName, folder, extension, language, chunkType, symbol, startLine, endLine, content, contentPreview, importance " +
                "FROM chunks WHERE collection = $c AND (symbol = $sym OR content LIKE $like) ORDER BY importance DESC LIMIT $limit";
            command.Parameters.AddWithValue("$c", collectionName);
            command.Parameters.AddWithValue("$sym", symbol);
            command.Parameters.AddWithValue("$like", $"%{symbol}%");
            command.Parameters.AddWithValue("$limit", Math.Max(1, perSymbol));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var hit = ReadRetrievalResult(reader, 0.99, "prompt symbol");
                results.Add(hit);
            }
        }

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(results);
    }

    public Task<IReadOnlyList<RetrievalResult>> SearchByTermsAsync(string collectionName, IReadOnlyList<string> terms, int perTerm, CancellationToken cancellationToken)
    {
        if (terms.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        var results = new List<RetrievalResult>();
        foreach (var term in terms.Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
        {
            using var command = _connection.CreateCommand();
            command.CommandText =
                "SELECT filePath, fileName, folder, extension, language, chunkType, symbol, startLine, endLine, content, contentPreview, importance " +
                "FROM chunks WHERE collection = $c AND (" +
                "LOWER(symbol) LIKE $like OR LOWER(filePath) LIKE $like OR LOWER(content) LIKE $like OR LOWER(contentPreview) LIKE $like" +
                ") ORDER BY importance DESC, startLine ASC LIMIT $limit";
            command.Parameters.AddWithValue("$c", collectionName);
            command.Parameters.AddWithValue("$like", $"%{term.ToLowerInvariant()}%");
            command.Parameters.AddWithValue("$limit", Math.Max(1, perTerm));

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var hit = ReadRetrievalResult(reader, 0.94, $"AST term '{term}'");
                results.Add(hit);
            }
        }

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(results);
    }

    public Task UpsertSymbolGraphAsync(string collectionName, ProjectGraph graph, CancellationToken cancellationToken)
    {
        using var transaction = _connection.BeginTransaction();
        using (var clearNodes = _connection.CreateCommand())
        {
            clearNodes.CommandText = "DELETE FROM symbol_nodes WHERE collection = $c";
            clearNodes.Parameters.AddWithValue("$c", collectionName);
            clearNodes.ExecuteNonQuery();
        }

        using (var clearEdges = _connection.CreateCommand())
        {
            clearEdges.CommandText = "DELETE FROM symbol_edges WHERE collection = $c";
            clearEdges.Parameters.AddWithValue("$c", collectionName);
            clearEdges.ExecuteNonQuery();
        }

        using var nodeCommand = _connection.CreateCommand();
        nodeCommand.CommandText =
            "INSERT INTO symbol_nodes (collection, nodeId, nodeType, label, filePath) VALUES ($c, $id, $type, $label, $path)";
        var pCollection = nodeCommand.Parameters.Add("$c", SqliteType.Text);
        var pId = nodeCommand.Parameters.Add("$id", SqliteType.Text);
        var pType = nodeCommand.Parameters.Add("$type", SqliteType.Text);
        var pLabel = nodeCommand.Parameters.Add("$label", SqliteType.Text);
        var pPath = nodeCommand.Parameters.Add("$path", SqliteType.Text);

        foreach (var node in graph.Nodes)
        {
            pCollection.Value = collectionName;
            pId.Value = node.Id;
            pType.Value = node.Type.ToString();
            pLabel.Value = node.Label;
            pPath.Value = node.Path ?? string.Empty;
            nodeCommand.ExecuteNonQuery();
        }

        using var edgeCommand = _connection.CreateCommand();
        edgeCommand.CommandText =
            "INSERT INTO symbol_edges (collection, fromId, toId, edgeType) VALUES ($c, $from, $to, $type)";
        var eCollection = edgeCommand.Parameters.Add("$c", SqliteType.Text);
        var eFrom = edgeCommand.Parameters.Add("$from", SqliteType.Text);
        var eTo = edgeCommand.Parameters.Add("$to", SqliteType.Text);
        var eType = edgeCommand.Parameters.Add("$type", SqliteType.Text);

        foreach (var edge in graph.Edges)
        {
            eCollection.Value = collectionName;
            eFrom.Value = edge.From;
            eTo.Value = edge.To;
            eType.Value = edge.Type.ToString();
            edgeCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    public Task<(int Nodes, int Edges)> GetSymbolGraphStatsAsync(string collectionName, CancellationToken cancellationToken)
    {
        using var nodes = _connection.CreateCommand();
        nodes.CommandText = "SELECT COUNT(*) FROM symbol_nodes WHERE collection = $c";
        nodes.Parameters.AddWithValue("$c", collectionName);
        var nodeCount = Convert.ToInt32(nodes.ExecuteScalar() ?? 0);

        using var edges = _connection.CreateCommand();
        edges.CommandText = "SELECT COUNT(*) FROM symbol_edges WHERE collection = $c";
        edges.Parameters.AddWithValue("$c", collectionName);
        var edgeCount = Convert.ToInt32(edges.ExecuteScalar() ?? 0);

        return Task.FromResult((nodeCount, edgeCount));
    }

    public Task DeleteFileAsync(string collectionName, string filePath, CancellationToken cancellationToken)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM chunks WHERE collection = $c AND filePath = $fp";
        command.Parameters.AddWithValue("$c", collectionName);
        command.Parameters.AddWithValue("$fp", filePath);
        command.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task ClearCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        ClearCollection(collectionName);
        return Task.CompletedTask;
    }

    private void ClearCollection(string collectionName)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM chunks WHERE collection = $c; DELETE FROM symbol_nodes WHERE collection = $c; DELETE FROM symbol_edges WHERE collection = $c;";
        command.Parameters.AddWithValue("$c", collectionName);
        command.ExecuteNonQuery();
    }

    private int ReadDimension(string collectionName)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT dim FROM collection_meta WHERE collection = $c";
        command.Parameters.AddWithValue("$c", collectionName);
        var value = command.ExecuteScalar();
        return value is null || value is DBNull ? 0 : Convert.ToInt32(value);
    }

    private void InitializeSchema()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS chunks (" +
            "id TEXT PRIMARY KEY, collection TEXT NOT NULL, filePath TEXT, fileName TEXT, folder TEXT, extension TEXT, " +
            "language TEXT, chunkType TEXT, symbol TEXT, startLine INTEGER, endLine INTEGER, content TEXT, contentPreview TEXT, " +
            "importance REAL, fileHash TEXT, chunkHash TEXT, vector BLOB);" +
            "CREATE INDEX IF NOT EXISTS idx_chunks_collection ON chunks(collection);" +
            "CREATE INDEX IF NOT EXISTS idx_chunks_file ON chunks(collection, filePath);" +
            "CREATE TABLE IF NOT EXISTS collection_meta (collection TEXT PRIMARY KEY, dim INTEGER);" +
            "CREATE TABLE IF NOT EXISTS symbol_nodes (collection TEXT NOT NULL, nodeId TEXT NOT NULL, nodeType TEXT, label TEXT, filePath TEXT, PRIMARY KEY(collection, nodeId));" +
            "CREATE TABLE IF NOT EXISTS symbol_edges (collection TEXT NOT NULL, fromId TEXT NOT NULL, toId TEXT NOT NULL, edgeType TEXT, PRIMARY KEY(collection, fromId, toId, edgeType));" +
            "CREATE INDEX IF NOT EXISTS idx_symbol_edges_collection ON symbol_edges(collection);";
        command.ExecuteNonQuery();
    }

    private static byte[] ToBlob(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBlob(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private static double Cosine(float[] a, float[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA <= 0 || normB <= 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
