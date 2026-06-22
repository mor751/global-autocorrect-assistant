namespace Autocorrect.Core.Brain;

public sealed class BrainWebStatus
{
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RagMode { get; set; } = string.Empty;
    public int VectorCount { get; set; }
    public int VectorDimension { get; set; }
    public int SymbolNodes { get; set; }
    public int SymbolEdges { get; set; }
    public int IndexedFiles { get; set; }
    public int TotalChunks { get; set; }
    public string? Framework { get; set; }
    public string? LastIndexedAt { get; set; }
}

public sealed class BrainWebVectorNode
{
    public int Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string ChunkType { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}

public sealed class BrainWebVectorMap
{
    public int Count { get; set; }
    public int Dimension { get; set; }
    public int EdgeCount { get; set; }
    public List<BrainWebVectorNode> Nodes { get; set; } = new();
    public List<int[]> Edges { get; set; } = new();
    public List<BrainWebFolderLegend> Folders { get; set; } = new();
}

public sealed class BrainWebFolderLegend
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Color { get; set; } = string.Empty;
}

public sealed class BrainWebGraphNode
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Path { get; set; }
}

public sealed class BrainWebGraphEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

public sealed class BrainWebSymbolGraph
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public List<BrainWebGraphNode> Nodes { get; set; } = new();
    public List<BrainWebGraphEdge> Edges { get; set; } = new();
}

public sealed class BrainWebSearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int TopK { get; set; } = 12;
}

// Builds JSON payloads for the localhost brain UI from indexed project data.
public static class BrainWebSnapshotBuilder
{
    private static readonly NodeType[] GraphFocusTypes =
    [
        NodeType.Function,
        NodeType.Component,
        NodeType.Route,
        NodeType.Hook,
        NodeType.Api,
        NodeType.File
    ];

    public static BrainWebStatus BuildStatus(ProjectIndexMetadata? metadata, BrainDoctorReport report) =>
        new()
        {
            ProjectName = metadata?.ProjectName ?? Path.GetFileName(report.ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ProjectRoot = report.ProjectRoot,
            Status = report.Status.ToString(),
            RagMode = report.RagMode,
            VectorCount = (int)Math.Min(report.VectorCount, int.MaxValue),
            VectorDimension = report.EmbedderDimension,
            SymbolNodes = report.SymbolNodes,
            SymbolEdges = report.SymbolEdges,
            IndexedFiles = report.IndexedFiles,
            TotalChunks = report.TotalChunks,
            Framework = metadata?.Framework,
            LastIndexedAt = metadata?.LastIndexedAt?.ToString("u")
        };

    public static BrainWebStatus BuildStatus(ProjectIndexMetadata? metadata, VectorStoreStats stats, string projectRoot, ProjectGraph? graph) =>
        new()
        {
            ProjectName = metadata?.ProjectName ?? Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ProjectRoot = projectRoot,
            Status = metadata?.Status.ToString() ?? ProjectBrainStatus.NoFolder.ToString(),
            RagMode = metadata?.CurrentRagMode() ?? "No brain",
            VectorCount = (int)Math.Min(stats.VectorCount, int.MaxValue),
            VectorDimension = stats.VectorDimension > 0 ? stats.VectorDimension : metadata?.VectorDimension ?? 0,
            SymbolNodes = graph?.Nodes.Count ?? 0,
            SymbolEdges = graph?.Edges.Count ?? 0,
            IndexedFiles = metadata?.IndexedFiles ?? 0,
            TotalChunks = metadata?.TotalChunks ?? 0,
            Framework = metadata?.Framework,
            LastIndexedAt = metadata?.LastIndexedAt?.ToString("u")
        };

    public static BrainWebVectorMap BuildVectorMap(IReadOnlyList<VectorPoint> points, VectorLayoutResult layout)
    {
        var folders = BuildFolderLegend(points);
        var nodes = new List<BrainWebVectorNode>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            var folder = FolderKey(points[i]);
            nodes.Add(new BrainWebVectorNode
            {
                Id = i,
                X = layout.X[i],
                Y = layout.Y[i],
                FilePath = points[i].FilePath,
                Folder = folder,
                ChunkType = points[i].ChunkType,
                Symbol = points[i].Symbol,
                StartLine = points[i].StartLine,
                EndLine = points[i].EndLine
            });
        }

        return new BrainWebVectorMap
        {
            Count = points.Count,
            Dimension = points.Count == 0 ? 0 : points[0].Vector.Length,
            EdgeCount = layout.Edges.Count,
            Nodes = nodes,
            Edges = layout.Edges.Select(edge => new[] { edge.A, edge.B }).ToList(),
            Folders = folders
        };
    }

    public static BrainWebSymbolGraph BuildSymbolGraph(ProjectGraph? graph, int maxNodes = 800)
    {
        if (graph is null || graph.Nodes.Count == 0)
        {
            return new BrainWebSymbolGraph();
        }

        var focus = graph.Nodes
            .Where(node => GraphFocusTypes.Contains(node.Type))
            .ToList();

        if (focus.Count == 0)
        {
            focus = graph.Nodes;
        }

        if (focus.Count > maxNodes)
        {
            var degree = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var edge in graph.Edges)
            {
                degree[edge.From] = degree.GetValueOrDefault(edge.From) + 1;
                degree[edge.To] = degree.GetValueOrDefault(edge.To) + 1;
            }

            focus = focus
                .OrderByDescending(node => degree.GetValueOrDefault(node.Id))
                .ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
                .Take(maxNodes)
                .ToList();
        }

        var allowed = focus.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = graph.Edges
            .Where(edge => allowed.Contains(edge.From) && allowed.Contains(edge.To))
            .Select(edge => new BrainWebGraphEdge
            {
                From = edge.From,
                To = edge.To,
                Type = edge.Type.ToString()
            })
            .ToList();

        return new BrainWebSymbolGraph
        {
            NodeCount = focus.Count,
            EdgeCount = edges.Count,
            Nodes = focus.Select(node => new BrainWebGraphNode
            {
                Id = node.Id,
                Type = node.Type.ToString(),
                Label = node.Label,
                Path = node.Path
            }).ToList(),
            Edges = edges
        };
    }

    private static List<BrainWebFolderLegend> BuildFolderLegend(IReadOnlyList<VectorPoint> points)
    {
        return points
            .GroupBy(FolderKey)
            .OrderByDescending(group => group.Count())
            .Take(12)
            .Select((group, index) => new BrainWebFolderLegend
            {
                Name = group.Key,
                Count = group.Count(),
                Color = HueColor(index, Math.Max(1, points.GroupBy(FolderKey).Count()))
            })
            .ToList();
    }

    private static string FolderKey(VectorPoint point)
    {
        var path = point.FilePath.Replace('\\', '/');
        var slash = path.IndexOf('/');
        return slash <= 0 ? "(root)" : path[..slash];
    }

    private static string HueColor(int index, int total)
    {
        var hue = index * 360.0 / total;
        var h = hue / 60.0;
        var x = 1 - Math.Abs(h % 2 - 1);
        double r = 0, g = 0, b = 0;
        switch ((int)h % 6)
        {
            case 0: r = 1; g = x; break;
            case 1: r = x; g = 1; break;
            case 2: g = 1; b = x; break;
            case 3: g = x; b = 1; break;
            case 4: r = x; b = 1; break;
            default: r = 1; b = x; break;
        }

        var red = (byte)(r * 210 + 30);
        var green = (byte)(g * 210 + 30);
        var blue = (byte)(b * 210 + 30);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }
}
