using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

internal partial class MakeProjectGroupViewModel : IDialog
{
    public string Title => Distribution.DisplayName;
    public bool CanResize => true;
    public event Action<bool>? CloseRequested;
    public IReadOnlyList<MakeProjectGroupItem> Projects { get; }
    public ObservableCollection<MakeProjectGroupItem> SelectedProjects { get; }
    [Notify] private bool useDefaultBuildOptions = true;
    public DelegateCommand BuildCommand { get; }
    public DelegateCommand CancelCommand { get; }

    public MakeProjectGroupViewModel(IReadOnlyList<ProjectDefinition> projects)
    {
        Projects = projects.Select(project => new MakeProjectGroupItem(project)).ToArray();
        SelectedProjects = new ObservableCollection<MakeProjectGroupItem>(Projects);
        BuildCommand = new DelegateCommand(() => CloseRequested?.Invoke(true), () => SelectedProjects.Count > 0);
        CancelCommand = new DelegateCommand(() => CloseRequested?.Invoke(false));
        SelectedProjects.CollectionChanged += (_, _) => BuildCommand.RaiseCanExecutedChanged();
    }
}

internal sealed class MakeProjectGroupItem(ProjectDefinition project)
{
    public ProjectDefinition Project { get; } = project;
    public string Name => string.IsNullOrEmpty(Project.AbsolutePath)
        ? Project.Name : Vb6ProjectPaths.FileNameOf(Project.AbsolutePath);
}
