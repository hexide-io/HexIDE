using System.Reflection;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.Components;

namespace HexIDE.Addins;

// The facade retains its owner even when an add-in registers a window after Initialize returns.
internal sealed class AddinOwnedHost(IHexIdeHost host, IToolWindowContributor tools, AddinOrigin origin) : IHexIdeHost,
    HexIDE.Language.ILanguageEditingRegistration, IRunEngineRegistration
{
    public IToolWindowContributor ToolWindows => tools;
    public IMenuContributor Menus { get; } = new OwnedMenus(host.Menus, origin);
    public IAddinEvents Events => host.Events;
    public IAddinRegistry Registry => host.Registry;
    public IEditorAccess Editor => host.Editor;
    public IProjectAccess Project => host.Project;
    public IDiagnosticsAccess Diagnostics => host.Diagnostics;
    public IOptionsPageContributor Options => host.Options;
    // HexIDE's own add-ins only (#4257): the caller supplies the approval callback, so any other add-in could answer yes for
    // the person. A developer-unsigned add-in exists only in a Debug build's developer mode, as RegisteredAddinTitle allows.
    public IServerChatTools ServerChatTools =>
        origin.IsFirstParty || origin.IsDeveloperUnsigned ? host.ServerChatTools : ServerChatToolsUnavailable.Instance;
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
    public IDisposable RegisterComponent(IComponentClass component) =>
        host is HexIdeHost concrete ? concrete.RegisterComponent(component, origin) : host.RegisterComponent(component);
    public IDisposable RegisterToolboxItem(IComponentClass component, string? iconKey = null) =>
        host is HexIdeHost concrete ? concrete.RegisterToolboxItem(component, iconKey, origin) : host.RegisterToolboxItem(component, iconKey);
    public IDisposable RegisterControlLibrary(IControlLibrary library, Func<IReadOnlyList<IComponentClass>> controls) =>
        host is HexIdeHost concrete ? concrete.RegisterControlLibrary(library, controls, origin) : host.RegisterControlLibrary(library, controls);
    public IDisposable RegisterProjectTemplate(IAddinProjectTemplate template) => host.RegisterProjectTemplate(template);
    public IAddinServerConnection RegisterLanguageServer(AddinLanguageServer server) =>
        host is HexIdeHost concrete ? concrete.RegisterLanguageServer(server, origin) : host.RegisterLanguageServer(server);
    public IDisposable RegisterDebugLaunch(AddinDebugLaunch launch) =>
        host is HexIdeHost concrete ? concrete.RegisterDebugLaunch(launch, origin) : host.RegisterDebugLaunch(launch);
    public IDisposable RegisterProjectType(AddinProjectType type) =>
        host is HexIdeHost concrete ? concrete.RegisterProjectType(type, origin) : host.RegisterProjectType(type);
    public void ReportFailure(string message, Exception? exception = null)
    {
        if (host is HexIdeHost concrete) concrete.ReportFailure(message, exception, origin);
        else host.ReportFailure(message, exception);
    }
    // NoInlining keeps this a real frame, so GetCallingAssembly is the add-in's, which the dictionary must belong to.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IDisposable RegisterResources(Uri dictionary) =>
        host is HexIdeHost concrete ? concrete.RegisterResources(dictionary, origin, Assembly.GetCallingAssembly())
            : host.RegisterResources(dictionary);
    // NoInlining keeps this a real frame, so GetCallingAssembly is the add-in's, which the packs must belong to.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IDisposable RegisterLanguagePacks(Uri folder) =>
        host is HexIdeHost concrete ? concrete.RegisterLanguagePacks(folder, origin, Assembly.GetCallingAssembly())
            : host.RegisterLanguagePacks(folder);
    public IDisposable RegisterDesignerPictures(Func<IDesignerDrawingSurface, IDesignerPictures> pictures) =>
        host is HexIdeHost concrete ? concrete.RegisterDesignerPictures(pictures, origin) : host.RegisterDesignerPictures(pictures);
    // Internal while the Basic provider is the only owner (#5403); checked against the owner's grant where it's registered.
    public IDisposable RegisterLanguageEditing(HexIDE.Language.LanguageEditing editing) =>
        host is HexIdeHost concrete ? concrete.RegisterLanguageEditing(editing, origin)
            : throw new InvalidOperationException("A language's editing helpers are registered only through the host an add-in is given.");

    // Internal while the Basic provider is the only engine (#5616); checked against the owner's grant where it's registered.
    public IDisposable RegisterRunEngine(Func<RunEngineServices, IRunEngine> engine) =>
        host is HexIdeHost concrete ? concrete.RegisterRunEngine(engine, origin)
            : throw new InvalidOperationException("The run engine is registered only through the host an add-in is given.");
    public bool RunProject() => host.RunProject();
    public bool StopProject() => host.StopProject();
}
