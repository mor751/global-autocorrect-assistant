namespace Autocorrect.App;

public sealed record RecentCorrection(
    DateTimeOffset When,
    string Original,
    string Replacement,
    double Confidence,
    string ProcessName,
    string Reason);

public sealed class RecentCorrectionStore
{
    private const int Limit = 50;
    private readonly object _gate = new();
    private readonly List<RecentCorrection> _items = new();

    public void Add(RecentCorrection correction)
    {
        lock (_gate)
        {
            _items.Insert(0, correction);
            if (_items.Count > Limit)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }
    }

    public IReadOnlyList<RecentCorrection> Snapshot()
    {
        lock (_gate)
        {
            return _items.ToArray();
        }
    }
}
