using System.Collections.ObjectModel;
using System.Windows.Input;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Addins;

public sealed partial class AddinMenuItemViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [Notify] private string header = "";
    public ICommand? Command { get; }
    public ObservableCollection<AddinMenuItemViewModel> SubItems { get; } = [];

    internal AddinMenuItemViewModel(string header, Action? onInvoke, Func<bool>? canExecute)
    {
        Header = header;
        Command = onInvoke is null ? null : new DelegateCommand(onInvoke, canExecute);
    }
}
