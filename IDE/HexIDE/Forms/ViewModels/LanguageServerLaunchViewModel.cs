using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

internal sealed partial class LanguageServerLaunchViewModel(
    string serverId, string serverName, string command, ILocalizationService localization)
    : ObservableObject, IDialog
{
    public string Title => localization.GetString("Str.Tool.LanguageServers.Trial.ConfirmTitle");
    public string Prompt => localization.GetString("Str.Tool.LanguageServers.Trial.ConfirmPrompt");
    public string Server => $"{serverName} [{serverId}]";
    public string Command { get; } = command;
    public bool CanResize => true;
    public bool Agreed { get; private set; }
    public event Action<bool>? CloseRequested;

    [RelayCommand] private void Run()
    {
        Agreed = true;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand] private void Cancel()
    {
        Agreed = false;
        CloseRequested?.Invoke(false);
    }
}
