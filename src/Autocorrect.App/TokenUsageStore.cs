using System.IO;
using System.Text.Json;

namespace Autocorrect.App;

public sealed record TokenUsageEntry(DateTime TimestampUtc, string Action, int TokensBefore, int TokensAfter)
{
    public int TokensSaved => Math.Max(0, TokensBefore - TokensAfter);
    public int ReductionPercent => TokensBefore <= 0 ? 0 : Math.Max(0, (int)Math.Round((1 - TokensAfter / (double)TokensBefore) * 100));
}

public sealed class TokenUsageState
{
    public int TotalRewrites { get; set; }
    public long TotalTokensBefore { get; set; }
    public long TotalTokensAfter { get; set; }
    public Dictionary<string, int> ByAction { get; set; } = new();
    public List<TokenUsageEntry> Recent { get; set; } = new();
}

// Persists prompt-compression analytics so the dashboard can show real savings over time.
public sealed class TokenUsageStore
{
    private const int RecentLimit = 40;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string _path;
    private readonly object _gate = new();
    private TokenUsageState _state;

    public TokenUsageStore()
    {
        Directory.CreateDirectory(SettingsRepository.DataDirectory);
        _path = Path.Combine(SettingsRepository.DataDirectory, "token-usage.json");
        _state = Load();
    }

    // Rough tokenizer-free estimate of ~4 characters per token.
    public static int EstimateTokens(string text) => string.IsNullOrEmpty(text) ? 0 : Math.Max(1, (int)Math.Round(text.Length / 4.0));

    public void Record(string action, string original, string rewritten)
    {
        var before = EstimateTokens(original);
        var after = EstimateTokens(rewritten);
        lock (_gate)
        {
            _state.TotalRewrites++;
            _state.TotalTokensBefore += before;
            _state.TotalTokensAfter += after;
            _state.ByAction[action] = _state.ByAction.TryGetValue(action, out var count) ? count + 1 : 1;
            _state.Recent.Insert(0, new TokenUsageEntry(DateTime.UtcNow, action, before, after));
            if (_state.Recent.Count > RecentLimit)
            {
                _state.Recent.RemoveRange(RecentLimit, _state.Recent.Count - RecentLimit);
            }

            Save();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _state = new TokenUsageState();
            Save();
        }
    }

    public TokenUsageState Snapshot()
    {
        lock (_gate)
        {
            return new TokenUsageState
            {
                TotalRewrites = _state.TotalRewrites,
                TotalTokensBefore = _state.TotalTokensBefore,
                TotalTokensAfter = _state.TotalTokensAfter,
                ByAction = new Dictionary<string, int>(_state.ByAction),
                Recent = new List<TokenUsageEntry>(_state.Recent)
            };
        }
    }

    private TokenUsageState Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<TokenUsageState>(File.ReadAllText(_path), JsonOptions) ?? new TokenUsageState()
                : new TokenUsageState();
        }
        catch
        {
            return new TokenUsageState();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_state, JsonOptions));
        }
        catch
        {
            // Analytics persistence must never crash the app.
        }
    }
}
