namespace Autocorrect.App;

public sealed class RuntimeStatusStore
{
    private readonly object _gate = new();

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    public DateTimeOffset LastInputAt { get; private set; }

    public DateTimeOffset? LastErrorAt { get; private set; }

    public string LastError { get; private set; } = string.Empty;

    public long InputEvents { get; private set; }

    public long Corrections { get; private set; }

    public long SkippedSlowCorrections { get; private set; }

    public long Errors { get; private set; }

    public void RecordInput()
    {
        lock (_gate)
        {
            InputEvents++;
            LastInputAt = DateTimeOffset.Now;
        }
    }

    public void RecordCorrection()
    {
        lock (_gate)
        {
            Corrections++;
        }
    }

    public void RecordSlowSkip()
    {
        lock (_gate)
        {
            SkippedSlowCorrections++;
        }
    }

    public void RecordError(Exception exception)
    {
        lock (_gate)
        {
            Errors++;
            LastErrorAt = DateTimeOffset.Now;
            LastError = exception.Message;
        }
    }

    public RuntimeStatusSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new RuntimeStatusSnapshot(
                StartedAt,
                LastInputAt,
                LastErrorAt,
                LastError,
                InputEvents,
                Corrections,
                SkippedSlowCorrections,
                Errors);
        }
    }
}

public sealed record RuntimeStatusSnapshot(
    DateTimeOffset StartedAt,
    DateTimeOffset LastInputAt,
    DateTimeOffset? LastErrorAt,
    string LastError,
    long InputEvents,
    long Corrections,
    long SkippedSlowCorrections,
    long Errors);
