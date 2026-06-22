using Autocorrect.Core.Brain;

namespace Autocorrect.Cli.Brain;

internal sealed class BrainSnapshotCache
{
    private readonly CliContext _context;
    private readonly string _projectRoot;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private BrainWebVectorMap? _vectorMap;
    private BrainWebSymbolGraph? _symbolGraph;
    private BrainWebStatus? _status;

    public BrainSnapshotCache(CliContext context, string projectRoot)
    {
        _context = context;
        _projectRoot = projectRoot;
    }

    public BrainWebStatus? Status => _status;
    public bool IsReady => _vectorMap is not null && _symbolGraph is not null;

    // Loads status, vector map, and symbol graph once before the browser opens.
    public async Task WarmAsync(CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            _status = await BuildStatusAsync(cancellationToken);
            Console.WriteLine($"Brain: {_status.VectorCount:N0} vectors, {_status.SymbolNodes:N0} AST nodes");

            Console.WriteLine("Brain: projecting vector map…");
            _vectorMap = await BuildVectorMapAsync(cancellationToken);
            Console.WriteLine($"Brain: { _vectorMap.Count:N0} nodes, {_vectorMap.EdgeCount:N0} semantic links");

            Console.WriteLine("Brain: building AST graph…");
            _symbolGraph = BuildSymbolGraph();
            Console.WriteLine($"Brain: {_symbolGraph.NodeCount:N0} nodes, {_symbolGraph.EdgeCount:N0} edges");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _vectorMap = null;
        _symbolGraph = null;
        _status = null;
        await WarmAsync(cancellationToken);
    }

    public async Task<BrainWebStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (_status is not null)
        {
            return _status;
        }

        return await BuildStatusAsync(cancellationToken);
    }

    public async Task<BrainWebVectorMap> GetVectorMapAsync(CancellationToken cancellationToken)
    {
        if (_vectorMap is not null)
        {
            return _vectorMap;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            _vectorMap ??= await BuildVectorMapAsync(cancellationToken);
            return _vectorMap;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task<BrainWebSymbolGraph> GetSymbolGraphAsync(CancellationToken cancellationToken)
    {
        if (_symbolGraph is not null)
        {
            return _symbolGraph;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            _symbolGraph ??= BuildSymbolGraph();
            return _symbolGraph;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<BrainWebStatus> BuildStatusAsync(CancellationToken cancellationToken)
    {
        var metadata = _context.Brain.LoadIndexMetadata(_projectRoot);
        var stats = await _context.Brain.GetVectorStatsAsync(_projectRoot, cancellationToken);
        var brain = _context.Brain.LoadBrain(_projectRoot);
        return BrainWebSnapshotBuilder.BuildStatus(metadata, stats, _projectRoot, brain?.Graph);
    }

    private async Task<BrainWebVectorMap> BuildVectorMapAsync(CancellationToken cancellationToken)
    {
        var points = await _context.Brain.ExportVectorsAsync(_projectRoot, cancellationToken);
        if (points.Count == 0)
        {
            return new BrainWebVectorMap();
        }

        var neighbors = points.Count > 2000 ? 2 : 3;
        var layout = await Task.Run(() => VectorBrainLayout.Compute(points, neighbors), cancellationToken);
        return BrainWebSnapshotBuilder.BuildVectorMap(points, layout);
    }

    private BrainWebSymbolGraph BuildSymbolGraph()
    {
        var brain = _context.Brain.LoadBrain(_projectRoot);
        return BrainWebSnapshotBuilder.BuildSymbolGraph(brain?.Graph);
    }
}
