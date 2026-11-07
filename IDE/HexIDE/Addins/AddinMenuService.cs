using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Serilog;

namespace HexIDE.Addins;

public sealed class AddinMenuService : IMenuContributor
{
    private readonly ObservableCollection<AddinMenuItemViewModel> _toolsItems = [];
    private readonly ObservableCollection<AddinMenuItemViewModel> _addInsItems = [];

    public ObservableCollection<AddinMenuItemViewModel> ToolsItems => _toolsItems;
    public ObservableCollection<AddinMenuItemViewModel> AddInsItems => _addInsItems;

    // Preserve the caller frame so diagnostics identify the contributing add-in.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IDisposable Add(string parentPath, string label, object? icon, Action onInvoke,
                           Func<bool>? canExecute = null)
    {
        var parts = parentPath.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        var topLevel = parts.Length == 0 ? "" : parts[0];
        if (!topLevel.Equals("Tools", StringComparison.OrdinalIgnoreCase) &&
            !topLevel.Equals("AddIns", StringComparison.OrdinalIgnoreCase))
        {
            var caller = Assembly.GetCallingAssembly();
            var contributor = AssemblyLoadContext.GetLoadContext(caller) is AddinLoadContext context
                ? context.Name : caller.GetName().Name;
            Log.Warning("AddinMenuService: add-in {Addin} requested unsupported menu location {ParentPath}; placement dropped",
                contributor, parentPath);
            return new ActionDisposable(() => { });
        }

        return topLevel.ToUpperInvariant() switch
        {
            "TOOLS" => AddFlat(_toolsItems, label, onInvoke, canExecute),
            "ADDINS" when parts.Length == 1 => AddFlat(_addInsItems, label, onInvoke, canExecute),
            _ => AddToSubmenu(_addInsItems, parts[1], label, onInvoke, canExecute),
        };
    }

    private static IDisposable AddFlat(ObservableCollection<AddinMenuItemViewModel> collection,
        string label, Action onInvoke, Func<bool>? canExecute)
    {
        var vm = new AddinMenuItemViewModel(label, onInvoke, canExecute);
        collection.Add(vm);
        var handle = new ActionDisposable(() => collection.Remove(vm));
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
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

        var handle = new ActionDisposable(() =>
        {
            submenu.SubItems.Remove(item);
            if (created && submenu.SubItems.Count == 0)
                parent.Remove(submenu);
        });
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }
}
