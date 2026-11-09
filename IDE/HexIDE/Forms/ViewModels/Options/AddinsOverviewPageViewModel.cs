using System;
using System.Collections.Generic;
using System.IO;
using HexIDE.Addins;

namespace HexIDE.Forms.ViewModels.Options;

public class AddinsOverviewPageViewModel(IReadOnlyList<AddinInfo> addins)
{
    public IReadOnlyList<AddinInfo> Addins { get; } = addins;
    public bool HasAddins => Addins.Count > 0;
    public string InstallationDirectory => Path.Combine(AppContext.BaseDirectory, "addins");
}
