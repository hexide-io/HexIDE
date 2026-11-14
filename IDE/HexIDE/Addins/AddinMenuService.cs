using HexIDE.Localization;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Serilog;

namespace HexIDE.Addins;

public sealed class AddinMenuService(ILocalizationService? localization = null) : IMenuContributor
{
    private readonly ILocalizationService _localization = localization ?? new LocalizationService();
    private readonly ObservableCollection<AddinMenuItemViewModel> _toolsItems = [];
    private readonly ObservableCollection<AddinMenuItemViewModel> _addInsItems = [];

    public ObservableCollection<AddinMenuItemViewModel> ToolsItems => _toolsItems;
    public ObservableCollection<AddinMenuItemViewModel> AddInsItems => _addInsItems;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public IDisposable Add(string parentPath, AddinTitle label, object? icon, Action onInvoke, Func<bool>? canExecute = null) =>
        AddOwned(default, parentPath, label, icon, onInvoke, canExecute, Assembly.GetCallingAssembly());

    internal IDisposable AddOwned(AddinOrigin origin, string parentPath, AddinTitle label, object? icon, Action onInvoke, Func<bool>? canExecute, Assembly? caller = null)
    {
        var text = new RegisteredAddinTitle(label, origin);
        AddinMenuItemViewModel? item = null;
        var handle = AddCore(parentPath, text.Resolve(_localization), icon, onInvoke, canExecute, vm => item = vm, caller);
        if (item is null) return handle;
        void Refresh() => item.Header = text.Resolve(_localization);
        _localization.LanguageChanged += Refresh;
        var localized = new ActionDisposable(() => { _localization.LanguageChanged -= Refresh; handle.Dispose(); });
        AddinInitializationScope.Record(localized.Dispose);
        return localized;
    }

    // Preserve the caller frame so diagnostics identify the contributing add-in.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IDisposable Add(string parentPath, string label, object? icon, Action onInvoke,
                           Func<bool>? canExecute = null)
        => AddOwned(default, parentPath, new AddinTitle(label), icon, onInvoke, canExecute, Assembly.GetCallingAssembly());

    private IDisposable AddCore(string parentPath, string label, object? icon, Action onInvoke, Func<bool>? canExecute, Action<AddinMenuItemViewModel>? registered, Assembly? caller)
    {
        var parts = parentPath.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        var topLevel = parts.Length == 0 ? "" : parts[0];
        if (!topLevel.Equals("Tools", StringComparison.OrdinalIgnoreCase) &&
            !topLevel.Equals("AddIns", StringComparison.OrdinalIgnoreCase))
        {
            caller ??= Assembly.GetCallingAssembly();
            var contributor = AssemblyLoadContext.GetLoadContext(caller) is AddinLoadContext context
                ? context.Name : caller.GetName().Name;
            Log.Warning("AddinMenuService: add-in {Addin} requested unsupported menu location {ParentPath}; placement dropped",
                contributor, parentPath);
            return new ActionDisposable(() => { });
        }

        return topLevel.ToUpperInvariant() switch
        {
            "TOOLS" => AddFlat(_toolsItems, label, onInvoke, canExecute, registered),
            "ADDINS" when parts.Length == 1 => AddFlat(_addInsItems, label, onInvoke, canExecute, registered),
            _ => AddToSubmenu(_addInsItems, parts[1], label, onInvoke, canExecute, registered),
        };
    }

    private static IDisposable AddFlat(ObservableCollection<AddinMenuItemViewModel> collection,
        string label, Action onInvoke, Func<bool>? canExecute, Action<AddinMenuItemViewModel>? registered)
    {
        var vm = new AddinMenuItemViewModel(label, onInvoke, canExecute);
        collection.Add(vm);
        registered?.Invoke(vm);
        var handle = new ActionDisposable(() => collection.Remove(vm));
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    private static IDisposable AddToSubmenu(ObservableCollection<AddinMenuItemViewModel> parent,
        string submenuHeader, string label, Action onInvoke, Func<bool>? canExecute, Action<AddinMenuItemViewModel>? registered)
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
        registered?.Invoke(item);

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
