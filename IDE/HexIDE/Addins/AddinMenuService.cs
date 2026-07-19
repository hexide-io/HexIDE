using System.Collections.ObjectModel;
using HexIDE.Addins;

namespace HexIDE.Addins;

public sealed class AddinMenuService : IMenuContributor
{
    private readonly ObservableCollection<AddinMenuItemViewModel> _toolsItems = [];
    private readonly ObservableCollection<AddinMenuItemViewModel> _addInsItems = [];

    public ObservableCollection<AddinMenuItemViewModel> ToolsItems => _toolsItems;
    public ObservableCollection<AddinMenuItemViewModel> AddInsItems => _addInsItems;

    public IDisposable Add(string parentPath, string label, object? icon, Action onInvoke,
                           Func<bool>? canExecute = null)
    {
        var parts = parentPath.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        var topLevel = parts[0];

        return topLevel.ToUpperInvariant() switch
        {
            "TOOLS" => AddFlat(_toolsItems, label, onInvoke, canExecute),
            "ADDINS" when parts.Length == 1 => AddFlat(_addInsItems, label, onInvoke, canExecute),
            "ADDINS" => AddToSubmenu(_addInsItems, parts[1], label, onInvoke, canExecute),
            _ => AddFlat(_addInsItems, label, onInvoke, canExecute),
        };
    }

    private static IDisposable AddFlat(ObservableCollection<AddinMenuItemViewModel> collection,
        string label, Action onInvoke, Func<bool>? canExecute)
    {
        var vm = new AddinMenuItemViewModel(label, onInvoke, canExecute);
        collection.Add(vm);
        return new ActionDisposable(() => collection.Remove(vm));
    }

    private static IDisposable AddToSubmenu(ObservableCollection<AddinMenuItemViewModel> parent,
        string submenuHeader, string label, Action onInvoke, Func<bool>? canExecute)
    {
        var submenu = parent.FirstOrDefault(x => x.Header == submenuHeader && x.Command is null);
        var created = submenu is null;
        if (created)
        {
            submenu = new AddinMenuItemViewModel(submenuHeader, null, null);
            parent.Add(submenu!);
        }

        var item = new AddinMenuItemViewModel(label, onInvoke, canExecute);
        submenu!.SubItems.Add(item);

        return new ActionDisposable(() =>
        {
            submenu.SubItems.Remove(item);
            if (created && submenu.SubItems.Count == 0)
                parent.Remove(submenu);
        });
    }
}
