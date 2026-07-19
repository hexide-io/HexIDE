using System.Collections.ObjectModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

/// <summary>
/// An on-disk directory node in the Project Explorer tree. Derived exclusively from member
/// paths (never disk enumeration), so an empty directory node cannot exist.
/// </summary>
public partial class DirectoryViewModel : IProjectTreeElement
{
    public ProjectViewModel? Project { get; }

    public string Name { get; }

    /// <summary>
    /// Project-relative directory path (e.g. <c>Forms\ui</c>) — the stable identity used to
    /// preserve expansion and selection across tree rebuilds. Compared case-insensitively.
    /// </summary>
    public string Key { get; }

    [Notify] private bool isExpanded = true;

    public ObservableCollection<IProjectTreeElement> Children { get; } = new();

    public DirectoryViewModel(ProjectViewModel? project, string name, string key)
    {
        Project = project;
        Name = name;
        Key = key;
    }
}
