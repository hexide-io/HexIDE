using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public sealed partial class ProjectFileViewModel(ProjectFileSource source) : ObservableObject, IProjectTreeElement
{
    public ProjectFileSource Source { get; } = source;
    public string Name => Path.GetFileName(Source.Path)!;
    public string AccessibleName => Name;
    [Notify] private bool isExpanded;
}
