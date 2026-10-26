using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.Forms.ViewModels;

public partial class WorkspaceArtifactConsentViewModel(string server, string path, ILocalizationService localization)
    : ObservableObject, IDialog
{
    public string Title => localization.GetString("Str.LspArtifact.Title");
    public string Message => string.Format(localization.GetString("Str.LspArtifact.Prompt"), server, path);
    public bool CanResize => false;
    public WorkspaceArtifactDecision Decision { get; private set; } = WorkspaceArtifactDecision.Deny;
    public event Action<bool>? CloseRequested;

    [RelayCommand] private void Allow() => Complete(WorkspaceArtifactDecision.Allow);
    [RelayCommand] private void Deny() => Complete(WorkspaceArtifactDecision.Deny);
    [RelayCommand] private void Always() => Complete(WorkspaceArtifactDecision.AllowAlways);
    private void Complete(WorkspaceArtifactDecision decision)
    {
        Decision = decision;
        CloseRequested?.Invoke(decision != WorkspaceArtifactDecision.Deny);
    }
}
