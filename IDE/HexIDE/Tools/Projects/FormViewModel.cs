using System;
using System.ComponentModel;
using System.IO;
using HexIDE.Runtime.ProjectElements;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class FormViewModel : ObservableObject, IDisposable, IProjectFileNode
{
    public ProjectViewModel Project { get; }
    public FormDefinition FormDefinition => form;

    public string? AbsolutePath => form.AbsolutePath;

    private readonly FormDefinition form;

    [Notify] private bool isExpanded;
    [Notify] private string name;
    [Notify] private string file;
    [Notify] private string? locationCaption;

    public FormViewModel(ProjectViewModel project, FormDefinition form)
    {
        Project = project;
        this.form = form;
        name = form.Name;
        file = form.AbsolutePath == null ? form.Name : Path.GetFileName(form.AbsolutePath);
        form.PropertyChanged += OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        Name = form.Name;
        File = form.AbsolutePath == null ? form.Name : Path.GetFileName(form.AbsolutePath);
    }

    public void Dispose()
    {
        form.PropertyChanged -= OnChanged;
    }
}