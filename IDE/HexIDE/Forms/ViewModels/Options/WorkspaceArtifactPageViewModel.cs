using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Addins;
using HexIDE.Localization;

namespace HexIDE.Forms.ViewModels.Options;

/// <summary>
/// Tools → Options → Workspace files: the persistent grants, each resettable. Like every other page's edit, a reset is
/// committed on OK and forgotten on Cancel (#2073).
/// </summary>
public partial class WorkspaceArtifactPageViewModel : ObservableObject, IOptionsPage
{
    public ObservableCollection<WorkspaceArtifactGrant> Grants { get; } = [];
    private readonly ConsentStore _store;
    private readonly ILocalizationService? _localization;
    private readonly List<string> _reset = [];
    public WorkspaceArtifactPageViewModel(ILocalizationService localization) : this(WorkspaceArtifactConsent.Store, localization) { }
    internal WorkspaceArtifactPageViewModel(ConsentStore store, ILocalizationService? localization = null)
    {
        _store = store;
        _localization = localization;
        LoadFromSettings();
    }

    public string Title => "Workspace files";

    public void LoadFromSettings()
    {
        _reset.Clear();
        Grants.Clear();
        foreach (var record in _store.Records)
        {
            var workspace = record.WorkspaceRoot ?? "";
            var file = record.ArtifactPath ?? "";
            Grants.Add(new(record.Id, workspace, record.ManifestHash, file, record.Provider ?? "",
                string.Format(_localization?.GetString("Str.LspArtifact.ResetGrant") ?? "Reset {0} for {1} in {2}",
                    record.Id, Path.GetFileName(file), workspace)));
        }
    }

    public void SaveToSettings()
    {
        foreach (var key in _reset) _store.Revoke(key);
        _reset.Clear();
    }

    /// <summary>The factory state is no persistent grant at all; previewed, and committed on OK like a reset.</summary>
    public void RestoreDefaults()
    {
        foreach (var grant in Grants) _reset.Add(grant.Key);
        Grants.Clear();
    }

    [RelayCommand]
    private void Reset(WorkspaceArtifactGrant grant)
    {
        _reset.Add(grant.Key);
        Grants.Remove(grant);
    }
}

/// <param name="Server">The server the grant is for.</param>
/// <param name="Workspace">The workspace it covers.</param>
/// <param name="Key">The consent key, which a reset revokes.</param>
/// <param name="File">The workspace-relative file it covers. Empty for a grant recorded before grants named one.</param>
/// <param name="Provider">The provider that produces the file.</param>
/// <param name="ResetName">The Reset button's accessible name, which says which grant it resets.</param>
public sealed record WorkspaceArtifactGrant(string Server, string Workspace, string Key, string File = "",
    string Provider = "", string ResetName = "");
