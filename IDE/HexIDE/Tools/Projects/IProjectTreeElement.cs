using System.ComponentModel;

namespace HexIDE.Tools;

public interface IProjectTreeElement : INotifyPropertyChanged
{
    public bool IsExpanded { get; set; }
}