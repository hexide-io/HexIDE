using System.Windows.Input;

namespace HexIDE.Forms.ViewModels;

public class RecentProjectItemViewModel(string header, ICommand command)
{
    public string Header { get; } = header;
    public ICommand Command { get; } = command;
}
