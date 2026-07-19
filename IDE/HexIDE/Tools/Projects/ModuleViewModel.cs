using System;
using System.ComponentModel;
using System.IO;
using HexIDE.Runtime.ProjectElements;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class ModuleViewModel : ObservableObject, IDisposable, IProjectFileNode
{
    public ProjectViewModel Project { get; }
    public ModuleDefinition ModuleDefinition => module;

    public string? AbsolutePath => module.AbsolutePath;

    private readonly ModuleDefinition module;

    [Notify] private bool isExpanded;
    [Notify] private string name;
    [Notify] private string file;

    public ModuleViewModel(ProjectViewModel project, ModuleDefinition module)
    {
        Project = project;
        this.module = module;
        name = module.Name;
        file = module.AbsolutePath == null ? module.Name : Path.GetFileName(module.AbsolutePath);
        module.PropertyChanged += OnChanged;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        Name = module.Name;
        File = module.AbsolutePath == null ? module.Name : Path.GetFileName(module.AbsolutePath);
    }

    public void Dispose()
    {
        module.PropertyChanged -= OnChanged;
    }
}
