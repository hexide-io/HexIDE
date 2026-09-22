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

    public RuntimeErrorViewModel(string errorText)
    {
        ErrorText = errorText;
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

    public bool CanContinue() => false;
    public bool CanEnd() => true;
    public bool CanDebug() => false;
    public bool CanHelp() => false;
}