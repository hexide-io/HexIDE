using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.Forms.ViewModels;

public class RecentProjectEntryViewModel : ObservableObject
{
    public string FullPath { get; }
    public string FileName { get; }
    public string Directory { get; }

    public RecentProjectEntryViewModel(string fullPath)
    {
        FullPath = fullPath;
        FileName = Path.GetFileName(fullPath);
        Directory = Path.GetDirectoryName(fullPath) ?? "";
    }
}
