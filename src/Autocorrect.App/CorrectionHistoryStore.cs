using System.IO;
using System.Text.Json;
using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class CorrectionHistoryStore : ICorrectionHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly int _limit;
    private readonly List<CorrectionHistoryEntry> _entries = new();

    public CorrectionHistoryStore(int limit)
    {
        _limit = Math.Max(20, limit);
        var directory = SettingsRepository.DataDirectory;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "correction-history.jsonl");
        Load();
    }

    public void Record(CorrectionHistoryEntry entry)
    {
        lock (_gate)
        {
            _entries.Insert(0, entry);
            while (_entries.Count > _limit)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }

            File.AppendAllText(_path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
    }

    public CorrectionHistoryEntry? Last()
    {
        lock (_gate)
        {
            return _entries.FirstOrDefault();
        }
    }

    public CorrectionHistoryEntry? PopLast()
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
            {
                return null;
            }

            var entry = _entries[0];
            _entries.RemoveAt(0);
            RewriteFile();
            return entry;
        }
    }

    public IReadOnlyList<CorrectionHistoryEntry> Snapshot(int limit)
    {
        lock (_gate)
        {
            return _entries.Take(limit).ToArray();
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        foreach (var line in File.ReadLines(_path).Reverse())
        {
            try
            {
                var entry = JsonSerializer.Deserialize<CorrectionHistoryEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    _entries.Add(entry);
                }
            }
            catch
            {
                // Ignore corrupted history lines; the file is local-only best effort.
            }

            if (_entries.Count >= _limit)
            {
                break;
            }
        }
    }

    private void RewriteFile()
    {
        var chronological = _entries.AsEnumerable().Reverse().Select(entry => JsonSerializer.Serialize(entry, JsonOptions));
        File.WriteAllLines(_path, chronological);
    }
}
