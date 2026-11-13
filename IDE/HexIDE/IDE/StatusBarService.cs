using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.Localization;

namespace HexIDE.IDE;

/// <summary>
/// Status bar state for the main IDE window.
/// Uses CommunityToolkit.Mvvm for <see cref="System.ComponentModel.INotifyPropertyChanged"/>.
/// </summary>
public partial class StatusBarService : ObservableObject, IStatusBarService
{
    private readonly ILocalizationService _localization;

    /// <summary>The localized idle message ("Ready"); tracks the active language.</summary>
    private string DefaultMessage => _localization.GetString("Str.StatusBar.Ready");

    private readonly object _messageGate = new();
    private string? _persistentMessage;
    private bool _temporaryMessageShowing;
    private long _temporaryGeneration;
    private string _message;
    private int? _line;
    private int? _column;
    private bool _insertMode = true;
    private Timer? _temporaryMessageTimer;

    public StatusBarService(ILocalizationService localization)
    {
        _localization = localization;
        _message = DefaultMessage;
        // Refresh the idle message live when the language changes, but only if it's currently showing.
        _localization.LanguageChanged += () =>
        {
            lock (_messageGate)
                if (_persistentMessage is null && !_temporaryMessageShowing) Message = DefaultMessage;
        };
    }

    private long _messageVersion;
    public long MessageVersion => Interlocked.Read(ref _messageVersion);

    public string Message
    {
        get => _message;
        private set
        {
            Interlocked.Increment(ref _messageVersion);
            SetProperty(ref _message, value);
        }
    }

    public int? Line
    {
        get => _line;
        set => SetProperty(ref _line, value);
    }

    public int? Column
    {
        get => _column;
        set => SetProperty(ref _column, value);
    }

    public bool InsertMode
    {
        get => _insertMode;
        set => SetProperty(ref _insertMode, value);
    }

    public void SetMessage(string message)
    {
        lock (_messageGate)
        {
            _persistentMessage = message;
            if (!_temporaryMessageShowing) Message = message;
        }
    }

    public void SetImmediateMessage(string message)
    {
        lock (_messageGate)
        {
            CancelTemporaryTimer();
            _persistentMessage = message;
            Message = message;
        }
    }

    public bool TryReplaceMessage(string expected, string? replacement)
    {
        lock (_messageGate)
        {
            if (_persistentMessage != expected) return false;
            _persistentMessage = replacement;
            if (!_temporaryMessageShowing) Message = replacement ?? DefaultMessage;
            return true;
        }
    }

    public void SetTemporaryMessage(string message, TimeSpan duration)
    {
        lock (_messageGate)
        {
            CancelTemporaryTimer();
            var generation = _temporaryGeneration;
            _temporaryMessageShowing = true;
            Message = message;
            _temporaryMessageTimer = new Timer(
                _ => RestorePersistentMessage(generation),
                null,
                duration,
                Timeout.InfiniteTimeSpan);
        }
    }

    public void ClearMessage()
    {
        lock (_messageGate)
        {
            CancelTemporaryTimer();
            _persistentMessage = null;
            Message = DefaultMessage;
        }
    }

    private void RestorePersistentMessage(long generation)
    {
        // Disposal does not stop a callback already queued on the thread pool.
        lock (_messageGate)
        {
            if (generation != _temporaryGeneration) return;
            CancelTemporaryTimer();
            Message = _persistentMessage ?? DefaultMessage;
        }
    }

    private void CancelTemporaryTimer()
    {
        _temporaryGeneration++;
        _temporaryMessageShowing = false;
        _temporaryMessageTimer?.Dispose();
        _temporaryMessageTimer = null;
    }
}
