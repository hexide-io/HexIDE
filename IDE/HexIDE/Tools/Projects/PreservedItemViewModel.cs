using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.Runtime.Serialization;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class PreservedItemViewModel : ObservableObject, IProjectFileNode
{
    public ProjectViewModel Project { get; }
    public string Line { get; }
    public string Name { get; }
    public string? AbsolutePath { get; }
    public bool IsMissing => AbsolutePath != null && !System.IO.File.Exists(AbsolutePath);
    public bool IsUnsupported { get; }
    public string AccessibleName => Line;

    [Notify] private bool isExpanded;
    [Notify] private string? locationCaption;

    public PreservedItemViewModel(ProjectViewModel project, string line)
    {
        Project = project;
        Line = line;
        var eq = line.IndexOf('=');
        var kind = eq < 0 ? "" : line[..eq].Trim();
        var value = eq < 0 ? line : line[(eq + 1)..].Trim();
        var semi = kind.Equals(SerializedProject.FormKey, StringComparison.OrdinalIgnoreCase) ? -1 : value.IndexOf(';');
        var path = semi < 0 ? value : value[(semi + 1)..].Trim();
        Name = semi < 0 ? SerializedProject.FileNameOf(path) : value[..semi].Trim();
        IsUnsupported = kind.Equals(SerializedProject.UserDocumentKey, StringComparison.OrdinalIgnoreCase);
        var directory = project.Definition.ItemLinesDirectory
            ?? (project.Definition.AbsolutePath is { } vbp ? Path.GetDirectoryName(vbp) : null);
        if (directory != null && path.Length > 0)
        {
            var hostPath = SerializedProject.ToHostPath(path);
            AbsolutePath = Path.GetFullPath(Path.IsPathRooted(hostPath) ? hostPath : Path.Combine(directory, hostPath));
        }
    }
}
