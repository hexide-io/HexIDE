using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tools;

public class ProjectGroupViewModel : ObservableObject, IDisposable
{
    /// <param name="title">The localized "Project Group - {0}", filled with the group's name.</param>
    public ProjectGroupViewModel(ProjectGroupDefinition group,
        ObservableCollection<ProjectViewModel> projects, string title)
    {
        Group = group;
        this.title = title;
        Group.PropertyChanged += OnChanged;
        Children = projects;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ProjectGroupDefinition.Name))
            OnPropertyChanged(nameof(DisplayName));
    }

    public void Dispose() => Group.PropertyChanged -= OnChanged;

    public ProjectGroupDefinition Group { get; }
    private readonly string title;

    public string DisplayName => string.Format(title, Group.Name);
    public ObservableCollection<ProjectViewModel> Children { get; }
}
