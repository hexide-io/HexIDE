using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// Says that HexIDE has revoked add-ins whose code is loaded, and offers to close HexIDE, which finishes removing them
/// (#5577). Close goes through the normal close, with its prompts to save; Later, Escape and the close box leave HexIDE
/// open. The add-ins' menus, commands and windows are already gone by the time this shows.
/// </summary>
public sealed partial class AddinRevokedNoticeViewModel(IReadOnlyList<string> addinTitles, ILocalizationService localization) : IDialog
{
    public string Title => localization.GetString("Str.Dialog.AddinRevoked.Title");
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public IReadOnlyList<string> AddinTitles { get; } = addinTitles;

    public string Message => string.Format(localization.GetString("Str.Dialog.AddinRevoked.Body"),
        string.Join(", ", AddinTitles));

    [RelayCommand] private void CloseIde() => CloseRequested?.Invoke(true);
    [RelayCommand] private void Later() => CloseRequested?.Invoke(false);
}
