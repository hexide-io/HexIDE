using System;
using System.ComponentModel;
using System.IO;
using HexIDE.Runtime.ProjectElements;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

/// <summary>
/// A Project Explorer node for a file the project carries but does not compile.
///
/// <para>
/// Deliberately a sibling of <see cref="ModuleViewModel"/> rather than a variant of it. The tree builder
/// only asks for <see cref="IProjectFileNode.Name"/> and <see cref="IProjectFileNode.AbsolutePath"/>, so
/// this node places itself in the filesystem hierarchy and picks up the out-of-cone location caption for
/// free — a related document is exactly the kind of member most likely to live outside the project's
/// directory.
/// </para>
/// </summary>
public partial class RelatedDocViewModel : ObservableObject, IDisposable, IProjectFileNode
{
    private readonly RelatedDocumentDefinition document;

    public ProjectViewModel Project { get; }
    public RelatedDocumentDefinition Definition => document;

    public string? AbsolutePath => document.AbsolutePath;

    [Notify] private bool isExpanded;
    [Notify] private string name;
    [Notify] private string file;
    [Notify] private string? locationCaption;

    public RelatedDocViewModel(ProjectViewModel project, RelatedDocumentDefinition document)
    {
        Project = project;
        this.document = document;
        name = document.Name;
        file = document.AbsolutePath is null ? document.Name : Path.GetFileName(document.AbsolutePath);
        document.PropertyChanged += OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        Name = document.Name;
        File = document.AbsolutePath is null ? document.Name : Path.GetFileName(document.AbsolutePath);
    }

    public void Dispose() => document.PropertyChanged -= OnChanged;
}
