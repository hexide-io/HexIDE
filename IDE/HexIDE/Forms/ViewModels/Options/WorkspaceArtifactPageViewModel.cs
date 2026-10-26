using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexIDE.IDE;
using HexIDE.Addins;

namespace HexIDE.Forms.ViewModels.Options;

public partial class WorkspaceArtifactPageViewModel : ObservableObject
{
    public ObservableCollection<WorkspaceArtifactGrant> Grants { get; } = [];
    private readonly ConsentStore _store;
    public WorkspaceArtifactPageViewModel() : this(WorkspaceArtifactConsent.Store) { }
    internal WorkspaceArtifactPageViewModel(ConsentStore store)
    {
        _store = store;
        foreach (var record in store.Records)
            Grants.Add(new(record.Id, record.WorkspaceRoot ?? "", record.ManifestHash));
    }

    [RelayCommand]
    private void Reset(WorkspaceArtifactGrant grant)
    {
        _store.Revoke(grant.Key);
        Grants.Remove(grant);
    }
}

public sealed record WorkspaceArtifactGrant(string Server, string Workspace, string Key);
