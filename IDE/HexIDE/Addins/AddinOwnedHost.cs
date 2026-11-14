using System.Reflection;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.Components;

namespace HexIDE.Addins;

// The facade retains its owner even when an add-in registers a window after Initialize returns.
internal sealed class AddinOwnedHost(IHexIdeHost host, IToolWindowContributor tools, AddinOrigin origin) : IHexIdeHost
{
    public IToolWindowContributor ToolWindows => tools;
    public IMenuContributor Menus { get; } = new OwnedMenus(host.Menus, origin);
    public IAddinEvents Events => host.Events;
    public IAddinRegistry Registry => host.Registry;
    public IEditorAccess Editor => host.Editor;
    public IProjectAccess Project => host.Project;
    public IDiagnosticsAccess Diagnostics => host.Diagnostics;
    public IOptionsPageContributor Options => host.Options;
    public IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        host.RegisterCommand(id, label, onInvoke, gesture, canExecute);
    public IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        host is HexIdeHost concrete ? concrete.RegisterCommand(id, label, onInvoke, gesture, canExecute, origin)
            : host.RegisterCommand(id, label, onInvoke, gesture, canExecute);

    private sealed class OwnedMenus(IMenuContributor menus, AddinOrigin owner) : IMenuContributor
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IDisposable Add(string parentPath, string label, object? icon, Action onInvoke, Func<bool>? canExecute = null) =>
            menus is AddinMenuService service ? service.AddOwned(owner, parentPath, new AddinTitle(label), icon, onInvoke, canExecute, Assembly.GetCallingAssembly())
                : menus.Add(parentPath, label, icon, onInvoke, canExecute);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IDisposable Add(string parentPath, AddinTitle label, object? icon, Action onInvoke, Func<bool>? canExecute = null) =>
            menus is AddinMenuService service ? service.AddOwned(owner, parentPath, label, icon, onInvoke, canExecute, Assembly.GetCallingAssembly())
                : menus.Add(parentPath, label, icon, onInvoke, canExecute);
    }
    public IDisposable RegisterComponent(IComponentClass component) => host.RegisterComponent(component);
    public IDisposable RegisterProjectTemplate(IAddinProjectTemplate template) => host.RegisterProjectTemplate(template);
    public IAddinServerConnection RegisterLanguageServer(AddinLanguageServer server) =>
        host is HexIdeHost concrete ? concrete.RegisterLanguageServer(server, origin) : host.RegisterLanguageServer(server);
    public IDisposable RegisterDebugLaunch(AddinDebugLaunch launch) =>
        host is HexIdeHost concrete ? concrete.RegisterDebugLaunch(launch, origin) : host.RegisterDebugLaunch(launch);
    public IDisposable RegisterProjectType(AddinProjectType type) =>
        host is HexIdeHost concrete ? concrete.RegisterProjectType(type, origin) : host.RegisterProjectType(type);
    public bool RunProject() => host.RunProject();
    public bool StopProject() => host.StopProject();
}
