using System.Collections.Generic;
using HexIDE.Addins;
using HexIDE.IDE;

namespace HexIDE.Forms.ViewModels.Options;

public class AddinsOverviewPageViewModel(IReadOnlyList<AddinInfo> addins)
{
    public IReadOnlyList<AddinInfo> Addins { get; } = addins;
    public bool HasAddins => Addins.Count > 0;
    public string InstallationDirectory => BundledAddinsPath.Directory;
}
