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

    private bool _isDefault = true;
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
            if (_isDefault) Message = DefaultMessage;
        };
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
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
        CancelTemporaryTimer();
        _isDefault = false;
        Message = message;
    }

    public void SetTemporaryMessage(string message, TimeSpan duration)
    {
        CancelTemporaryTimer();
        _isDefault = false;
        Message = message;

        _temporaryMessageTimer = new Timer(
            _ => RevertToDefault(),
            null,
            duration,
            Timeout.InfiniteTimeSpan);
    }

    public void ClearMessage()
    {
        CancelTemporaryTimer();
        _isDefault = true;
        Message = DefaultMessage;
    }

    private void RevertToDefault()
    {
        // Timer callback runs on a thread-pool thread; ObservableObject's SetProperty handles
        // cross-thread INPC fine, and the UI dispatcher marshals the binding update.
        CancelTemporaryTimer();
        _isDefault = true;
        Message = DefaultMessage;
    }

    private void CancelTemporaryTimer()
    {
        _temporaryMessageTimer?.Dispose();
        _temporaryMessageTimer = null;
    }
}
