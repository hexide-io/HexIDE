using System.Collections.ObjectModel;
using System.Windows.Input;
using HexIDE.Utils;

namespace HexIDE.Addins;

public sealed class AddinMenuItemViewModel
{
    public string Header { get; }
    public ICommand? Command { get; }
    public ObservableCollection<AddinMenuItemViewModel> SubItems { get; } = [];

    internal AddinMenuItemViewModel(string header, Action? onInvoke, Func<bool>? canExecute)
    {
        Header = header;
        Command = onInvoke is null ? null : new DelegateCommand(onInvoke, canExecute);
    }
}
