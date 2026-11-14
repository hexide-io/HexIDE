using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class ProjectGroupViewModel : ObservableObject, IDisposable, IProjectTreeElement
{
    /// <param name="title">The localized "Project Group - {0}", filled with the group's name.</param>
    public ProjectGroupViewModel(ProjectGroupDefinition group,
        ObservableCollection<ProjectViewModel> projects, string title)
    {
        Group = group;
        this.title = title;
        Group.PropertyChanged += OnChanged;
        this.projects = projects;
        projects.CollectionChanged += OnProjectsChanged;
        Rebuild();
    }

    private readonly ObservableCollection<ProjectViewModel> projects;

    private void OnProjectsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        if (Group.AbsolutePath != null)
            Children.Add(new ProjectFileViewModel(new ProjectFileSource(Group)));
        foreach (var project in projects) Children.Add(project);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ProjectGroupDefinition.AbsolutePath)) Rebuild();
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ProjectGroupDefinition.Name))
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AccessibleName));
        }
    }

    public void Dispose()
    {
        Group.PropertyChanged -= OnChanged;
        projects.CollectionChanged -= OnProjectsChanged;
    }

    public ProjectGroupDefinition Group { get; }
    private readonly string title;

    [Notify] private bool isExpanded = true;

    /// <inheritdoc/>
    public string AccessibleName => DisplayName;

    public string DisplayName => string.Format(title, Group.Name);
    public ObservableCollection<IProjectTreeElement> Children { get; } = new();
}
