using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// The Windows-display-style "Keep these settings?" gate for a live language switch. Language is the
/// one setting where a wrong pick can lock you out of the very menu that undoes it, so after applying a
/// new language we show this modal with a countdown that auto-reverts unless the user confirms.
/// <para>
/// The Keep / Revert labels and prompt are <b>captured bilingual strings</b> (the new language AND the
/// previous one), frozen at construction — deliberately NOT DynamicResource — so whichever language the
/// user can actually read, the escape hatch is legible.
/// </para>
/// </summary>
public partial class LanguageRevertGateViewModel : ObservableObject, IDialog
{
    private readonly string _countdownFormat;
    private int _secondsRemaining;
    private bool _closed;
    private DispatcherTimer? _timer;

    public string Title { get; }
    public bool CanResize => false;
    public string Prompt { get; }
    public string KeepLabel { get; }
    public string RevertLabel { get; }

    [ObservableProperty] private string _countdownText = "";

    public event Action<bool>? CloseRequested;

    public LanguageRevertGateViewModel(
        string title, string prompt, string keepLabel, string revertLabel, string countdownFormat, int seconds)
    {
        Title = title;
        Prompt = prompt;
        KeepLabel = keepLabel;
        RevertLabel = revertLabel;
        _countdownFormat = countdownFormat;
        _secondsRemaining = seconds;
        UpdateCountdown();
    }

    /// <summary>Builds a gate with Keep/Revert/prompt labelled in both the new and previous language.</summary>
    public static LanguageRevertGateViewModel Create(
        ILocalizationService loc, string previousId, string newId, int seconds = 15)
    {
        string Bilingual(string key)
        {
            var inNew = loc.GetStringFrom(newId, key);
            var inPrev = loc.GetStringFrom(previousId, key);
            return inNew == inPrev ? inNew : $"{inNew}  /  {inPrev}";
        }

        return new LanguageRevertGateViewModel(
            Bilingual("Str.RevertGate.Title"),
            Bilingual("Str.RevertGate.Prompt"),
            Bilingual("Str.RevertGate.Keep"),
            Bilingual("Str.RevertGate.Revert"),
            Bilingual("Str.RevertGate.Countdown"),
            seconds);
    }

    /// <summary>Seconds left before auto-revert (exposed for tests).</summary>
    public int SecondsRemaining => _secondsRemaining;

    /// <summary>Starts the 1-second countdown. Called by the view when it is shown.</summary>
    public void Start()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>Advances the countdown by one second; auto-reverts at zero. Driven by the timer (or tests).</summary>
    public void Tick()
    {
        if (_closed) return;
        _secondsRemaining--;
        if (_secondsRemaining <= 0)
        {
            Close(false);
            return;
        }
        UpdateCountdown();
    }

    [RelayCommand] private void Keep() => Close(true);

    [RelayCommand] private void Revert() => Close(false);

    private void UpdateCountdown() => CountdownText = string.Format(_countdownFormat, _secondsRemaining);

    private void Close(bool keep)
    {
        if (_closed) return;
        _closed = true;
        _timer?.Stop();
        CloseRequested?.Invoke(keep);
    }
}
