using System.Collections.ObjectModel;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tools;

public class ProjectGroupViewModel
{
    public ProjectGroupViewModel(ProjectGroupDefinition group,
        ObservableCollection<ProjectViewModel> projects)
    {
        Group = group;
        Children = projects;
    }

    public ProjectGroupDefinition Group { get; }
    public string DisplayName => $"Project Group - {Group.Name}";
    public ObservableCollection<ProjectViewModel> Children { get; }
}
