using System.Text;
using System.Threading.Channels;
using Autocorrect.Core;

namespace Autocorrect.App;

public sealed class AutocorrectController : IDisposable
{
    private const int PreviousWordLimit = 12;

    private readonly IKeyboardMonitor _keyboardMonitor;
    private readonly ITextContextDetector _contextDetector;
    private readonly ITextReplacer _textReplacer;
    private readonly IAsyncCorrectionEngine _correctionEngine;
    private readonly IWordSuggestionEngine? _suggestionEngine;
    private readonly CorrectionSettings _settings;
    private readonly RecentCorrectionStore _recentCorrections;
    private readonly RuntimeStatusStore _runtimeStatus;
    private readonly IWordLearningStore _learningStore;
    private readonly ICorrectionHistory _correctionHistory;
    private readonly ITextSuggestionPresenter _suggestionPresenter;
    private readonly IFloatingOverlayService _floatingOverlayService;
    private readonly Channel<QueuedKey> _queue = Channel.CreateBounded<QueuedKey>(new BoundedChannelOptions(512)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly StringBuilder _currentWord = new();
    private readonly StringBuilder _recentText = new();
    private readonly Queue<string> _previousWords = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _worker;
    private long _inputVersion;

    public AutocorrectController(
        IKeyboardMonitor keyboardMonitor,
        ITextContextDetector contextDetector,
        ITextReplacer textReplacer,
        IAsyncCorrectionEngine correctionEngine,
        CorrectionSettings settings,
        RecentCorrectionStore recentCorrections,
        RuntimeStatusStore runtimeStatus,
        IWordLearningStore? learningStore = null,
        ICorrectionHistory? correctionHistory = null,
        ITextSuggestionPresenter? suggestionPresenter = null,
        IFloatingOverlayService? floatingOverlayService = null)
    {
        _keyboardMonitor = keyboardMonitor;
        _contextDetector = contextDetector;
        _textReplacer = textReplacer;
        _correctionEngine = correctionEngine;
        _suggestionEngine = correctionEngine as IWordSuggestionEngine;
        _settings = settings;
        _recentCorrections = recentCorrections;
        _runtimeStatus = runtimeStatus;
        _learningStore = learningStore ?? NullWordLearningStore.Instance;
        _correctionHistory = correctionHistory ?? InMemoryCorrectionHistory.CreateDefault();
        _suggestionPresenter = suggestionPresenter ?? NullSuggestionPresenter.Instance;
        _floatingOverlayService = floatingOverlayService ?? NullFloatingOverlayService.Instance;
        _suggestionPresenter.SuggestionAccepted += OnSuggestionAccepted;
        _keyboardMonitor.KeyTyped += OnKeyTyped;
    }

    public void Start()
    {
        _worker ??= Task.Run(ProcessQueueAsync);
        _keyboardMonitor.Start();
    }

    public void Dispose()
    {
        _keyboardMonitor.KeyTyped -= OnKeyTyped;
        _suggestionPresenter.SuggestionAccepted -= OnSuggestionAccepted;
        _suggestionPresenter.Hide();
        _floatingOverlayService.Hide();
        _keyboardMonitor.Stop();
        _queue.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Shutdown should not crash the tray process.
        }

        _shutdown.Dispose();
    }

    private void OnKeyTyped(object? sender, TypedKeyEventArgs e)
    {
        if (e.Kind is TypedKeyKind.NavigationUp or TypedKeyKind.NavigationDown or TypedKeyKind.AcceptSuggestion &&
            _suggestionPresenter.IsVisible)
        {
            e.Handled = true;
        }

        var version = Interlocked.Increment(ref _inputVersion);
        _runtimeStatus.RecordInput();
        _queue.Writer.TryWrite(new QueuedKey(e, version));
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var queued in _queue.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
            {
                await ProcessKeyAsync(queued, _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _runtimeStatus.RecordError(ex);
        }
    }

    private async Task ProcessKeyAsync(QueuedKey queued, CancellationToken cancellationToken)
    {
        try
        {
            switch (queued.Event.Kind)
            {
                case TypedKeyKind.Character when queued.Event.Character is char c:
                    _currentWord.Append(c);
                    AppendRecent(c);
                    ShowSuggestions();
                    break;
                case TypedKeyKind.Backspace:
                    if (_currentWord.Length > 0)
                    {
                        _currentWord.Length--;
                    }

                    ShowSuggestions();
                    break;
                case TypedKeyKind.NavigationUp:
                    if (_suggestionPresenter.IsVisible)
                    {
                        _suggestionPresenter.MoveSelection(-1);
                    }
                    else
                    {
                        ResetCurrentWord();
                    }

                    break;
                case TypedKeyKind.NavigationDown:
                    if (_suggestionPresenter.IsVisible)
                    {
                        _suggestionPresenter.MoveSelection(1);
                    }
                    else
                    {
                        ResetCurrentWord();
                    }

                    break;
                case TypedKeyKind.AcceptSuggestion:
                    if (_suggestionPresenter.IsVisible && _suggestionPresenter.AcceptSelected())
                    {
                        break;
                    }

                    _suggestionPresenter.Hide();
                    ResetCurrentWord();
                    break;
                case TypedKeyKind.Delimiter when queued.Event.Character is char delimiter:
                    _suggestionPresenter.Hide();
                    AppendRecent(delimiter);
                    await HandleCompletedWordAsync(delimiter, queued.Version, cancellationToken).ConfigureAwait(false);
                    break;
                case TypedKeyKind.Reset:
                    _suggestionPresenter.Hide();
                    _floatingOverlayService.Hide();
                    ResetCurrentWord();
                    _recentText.Clear();
                    break;
            }
        }
        catch (Exception ex)
        {
            _runtimeStatus.RecordError(ex);
            ResetCurrentWord();
        }
    }

    private async Task HandleCompletedWordAsync(char delimiter, long delimiterVersion, CancellationToken cancellationToken)
    {
        var word = _currentWord.ToString();
        ResetCurrentWord();

        if (word.Length == 0)
        {
            return;
        }

        if (!_settings.Enabled || !_settings.AutocorrectEnabled)
        {
            RememberWord(word);
            _learningStore.RecordTypedWord(word);
            return;
        }

        var appContext = _contextDetector.GetActiveContext(_settings);
        if (appContext.IsSensitive || !appContext.IsAllowedForAutocorrect)
        {
            RememberWord(word);
            return;
        }

        var request = new CorrectionRequest(word, _previousWords.ToArray(), appContext, delimiter);
        var startedAt = Environment.TickCount64;
        var result = await _correctionEngine.CorrectAsync(request, _settings, cancellationToken).ConfigureAwait(false);
        if (result is null || !result.HasChange)
        {
            RememberWord(word);
            _learningStore.RecordTypedWord(word);
            _floatingOverlayService.Schedule(_recentText.ToString(), appContext);
            return;
        }

        var elapsed = Environment.TickCount64 - startedAt;
        if (elapsed > _settings.MaxCorrectionLatencyMs || Interlocked.Read(ref _inputVersion) != delimiterVersion)
        {
            _runtimeStatus.RecordSlowSkip();
            return;
        }

        if (!BrowserReplacementSafety.CanReplaceCompletedWord(
                appContext,
                result.Original,
                result.Replacement,
                delimiter,
                out var browserSafetyReason))
        {
            RememberWord(word);
            _learningStore.RecordTypedWord(word);
            _runtimeStatus.RecordSlowSkip();
            _recentCorrections.Add(new RecentCorrection(
                DateTimeOffset.Now,
                result.Original,
                result.Replacement,
                result.Confidence,
                appContext.ProcessName,
                browserSafetyReason));
            return;
        }

        var replacement = _textReplacer.ReplaceCompletedWord(result.Original, result.Replacement, delimiter);
        if (!replacement.Success)
        {
            _runtimeStatus.RecordError(new InvalidOperationException(replacement.Error ?? "Replacement failed."));
            return;
        }

        _runtimeStatus.RecordCorrection();
        RememberWord(result.Replacement);
        _learningStore.RecordAcceptedCorrection(result.Original, result.Replacement);
        _correctionHistory.Record(new CorrectionHistoryEntry(
            result.Original,
            result.Replacement,
            DateTimeOffset.Now,
            appContext.ProcessName,
            appContext.WindowTitle,
            string.Empty,
            result.Confidence,
            result.Source,
            replacement.Method));
        _recentCorrections.Add(new RecentCorrection(
            DateTimeOffset.Now,
            result.Original,
            result.Replacement,
            result.Confidence,
            appContext.ProcessName,
            result.Reason));
        _floatingOverlayService.Schedule(_recentText.ToString(), appContext);
    }

    public bool UndoLastCorrection()
    {
        var entry = _correctionHistory.PopLast();
        if (entry is null)
        {
            return false;
        }

        var replacement = _textReplacer.ReplaceCurrentWord(entry.Replacement, entry.Original);
        if (!replacement.Success)
        {
            _runtimeStatus.RecordError(new InvalidOperationException(replacement.Error ?? "Undo failed."));
            return false;
        }

        _learningStore.RecordRejectedCorrection(entry.Original, entry.Replacement);
        return true;
    }

    private void RememberWord(string word)
    {
        _previousWords.Enqueue(word);
        while (_previousWords.Count > PreviousWordLimit)
        {
            _previousWords.Dequeue();
        }
    }

    private void ResetCurrentWord()
    {
        _currentWord.Clear();
    }

    private void ShowSuggestions()
    {
        if (!_settings.ShowSuggestionPopup || !_settings.Enabled || !_settings.AutocorrectEnabled || _suggestionEngine is null)
        {
            _suggestionPresenter.Hide();
            return;
        }

        var word = _currentWord.ToString();
        if (word.Length < 2)
        {
            _suggestionPresenter.Hide();
            return;
        }

        var suggestions = _suggestionEngine.Suggest(word, _previousWords.ToArray(), _settings);
        _suggestionPresenter.Show(suggestions);
    }

    private void AppendRecent(char c)
    {
        _recentText.Append(c);
        if (_recentText.Length > 1200)
        {
            _recentText.Remove(0, _recentText.Length - 1200);
        }
    }

    private void OnSuggestionAccepted(object? sender, string suggestion)
    {
        try
        {
            var word = _currentWord.ToString();
            if (word.Length == 0 || string.Equals(word, suggestion, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var appContext = _contextDetector.GetActiveContext(_settings);
            if (!BrowserReplacementSafety.CanReplaceCurrentWord(appContext, word, suggestion, out var browserSafetyReason))
            {
                _recentCorrections.Add(new RecentCorrection(
                    DateTimeOffset.Now,
                    word,
                    suggestion,
                    0,
                    appContext.ProcessName,
                    browserSafetyReason));
                _suggestionPresenter.Hide();
                return;
            }

            var replacement = _textReplacer.ReplaceCurrentWord(word, suggestion);
            if (!replacement.Success)
            {
                _runtimeStatus.RecordError(new InvalidOperationException(replacement.Error ?? "Suggestion replacement failed."));
                return;
            }

            ResetCurrentWord();
            RememberWord(suggestion);
            _learningStore.RecordAcceptedCorrection(word, suggestion);
            _suggestionPresenter.Hide();
            _runtimeStatus.RecordCorrection();
        }
        catch (Exception ex)
        {
            _runtimeStatus.RecordError(ex);
        }
    }

    private sealed record QueuedKey(TypedKeyEventArgs Event, long Version);
}

public sealed class InMemoryCorrectionHistory : ICorrectionHistory
{
    private readonly List<CorrectionHistoryEntry> _entries = new();

    public static InMemoryCorrectionHistory CreateDefault() => new();

    public void Record(CorrectionHistoryEntry entry)
    {
        _entries.Insert(0, entry);
    }

    public CorrectionHistoryEntry? Last() => _entries.FirstOrDefault();

    public CorrectionHistoryEntry? PopLast()
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        var entry = _entries[0];
        _entries.RemoveAt(0);
        return entry;
    }

    public IReadOnlyList<CorrectionHistoryEntry> Snapshot(int limit)
    {
        return _entries.Take(limit).ToArray();
    }
}
