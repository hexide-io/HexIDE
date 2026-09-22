using System.ComponentModel;

namespace HexIDE.Tools;

public interface IProjectTreeElement : INotifyPropertyChanged
{
    public bool IsExpanded { get; set; }

    /// <summary>
    /// What the node is called to a screen reader and to automation: the text the tree shows for it. Without it
    /// every node was nameless, so a project's forms could be told apart only by position (#542).
    /// </summary>
    public string AccessibleName { get; }
}