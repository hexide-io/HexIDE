using System;
using HexIDE.IDE;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.Forms.ViewModels;

public class RuntimeErrorViewModel : ObservableObject, IDialog
{
    public string Title => "HexIDE";
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;
    public string ErrorText { get; }

    private readonly bool canDebug;

    /// <param name="canDebug">Whether Debug can break at the statement that raised the error: true while the program is
    /// halted there, with the procedure still live (#1011).</param>
    public RuntimeErrorViewModel(string errorText, bool canDebug = false)
    {
        ErrorText = errorText;
        this.canDebug = canDebug;
    }

    public void Continue()
    {
        CloseRequested?.Invoke(false);
    }

    /// <summary>Closes the dialog answering true, which is how whoever showed it knows to end the run (#600).</summary>
    public void End()
    {
        CloseRequested?.Invoke(true);
    }

    public void Debug()
    {
        CloseRequested?.Invoke(false);
    }

    public void Help()
    {
        CloseRequested?.Invoke(false);
    }

    // Avalonia pairs a method bound as a command with Can{Name} only when it takes one object parameter.
    // These took none, so none was ever consulted and all four buttons were enabled (#594).
    public bool CanContinue(object? parameter) => false;
    public bool CanEnd(object? parameter) => true;
    public bool CanDebug(object? parameter) => canDebug;
    public bool CanHelp(object? parameter) => false;
}