using System.Collections.Generic;
using HexIDE.Addins;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

/// <param name="revocationStatus">What the revocation checks found, or null when nothing is checked (#5577).</param>
public class AddinsOverviewPageViewModel(IReadOnlyList<AddinInfo> addins, string? revocationStatus = null)
{
    public IReadOnlyList<AddinInfo> Addins { get; } = addins;
    public string RevocationStatus { get; } = revocationStatus ?? "";
    public bool HasRevocationStatus => !string.IsNullOrEmpty(RevocationStatus);
    public bool HasAddins => Addins.Count > 0;
    public string InstallationDirectory => BundledAddinsPath.Directory;
}
