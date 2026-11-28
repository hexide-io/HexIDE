using System.Reflection;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.Components;

namespace HexIDE.Addins;

// The facade retains its owner even when an add-in registers a window after Initialize returns. Once the add-in is revoked
// mid-session (withdrawn), none of its commands, menu items or event handlers run, whenever they were registered (#5577).
internal sealed class AddinOwnedHost(IHexIdeHost host, IToolWindowContributor tools, AddinOrigin origin, Func<bool>? withdrawn = null)
    : IHexIdeHost, HexIDE.Language.ILanguageEditingRegistration, IRunEngineRegistration, HexIDE.IDE.IProjectMakerRegistration
{
    private readonly Func<bool> _withdrawn = withdrawn ?? (() => false);

    // Internal for phase 2 (#5621): only HexIDE's own host can check the owner's grant, so any other is refused.
    public IDisposable RegisterProjectMaker(HexIDE.IDE.IProjectMaker maker) =>
        host is HexIdeHost concrete ? concrete.RegisterProjectMaker(maker, origin)
            : throw new InvalidOperationException("A project maker is registered only through the host HexIDE gives an add-in.");

    public IToolWindowContributor ToolWindows => tools;
    public IMenuContributor Menus { get; } = new OwnedMenus(host.Menus, origin, withdrawn ?? (() => false));
    public IAddinEvents Events { get; } = new OwnedEvents(host.Events, withdrawn ?? (() => false));
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
        host.RegisterCommand(id, label, Guard(onInvoke, _withdrawn), gesture, Guard(canExecute, _withdrawn));
    public IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        host is HexIdeHost concrete
            ? concrete.RegisterCommand(id, label, Guard(onInvoke, _withdrawn), gesture, Guard(canExecute, _withdrawn), origin)
            : host.RegisterCommand(id, label, Guard(onInvoke, _withdrawn), gesture, Guard(canExecute, _withdrawn));

    // A withdrawn add-in's command or menu item never runs, if anything of it outlives the withdrawal, and one that says when it
    // can run says no. One with no such check is left without one, as registered: running it does nothing.
    private static Action Guard(Action onInvoke, Func<bool> withdrawn) => () => { if (!withdrawn()) onInvoke(); };
    private static Func<bool>? Guard(Func<bool>? canExecute, Func<bool> withdrawn) =>
        canExecute is null ? null : () => !withdrawn() && canExecute();

    private sealed class OwnedMenus(IMenuContributor menus, AddinOrigin owner, Func<bool> withdrawn) : IMenuContributor
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IDisposable Add(string parentPath, string label, object? icon, Action onInvoke, Func<bool>? canExecute = null) =>
            menus is AddinMenuService service
                ? service.AddOwned(owner, parentPath, new AddinTitle(label), icon, Guard(onInvoke, withdrawn), Guard(canExecute, withdrawn),
                    Assembly.GetCallingAssembly())
                : menus.Add(parentPath, label, icon, Guard(onInvoke, withdrawn), Guard(canExecute, withdrawn));
        [MethodImpl(MethodImplOptions.NoInlining)]
        public IDisposable Add(string parentPath, AddinTitle label, object? icon, Action onInvoke, Func<bool>? canExecute = null) =>
            menus is AddinMenuService service
                ? service.AddOwned(owner, parentPath, label, icon, Guard(onInvoke, withdrawn), Guard(canExecute, withdrawn),
                    Assembly.GetCallingAssembly())
                : menus.Add(parentPath, label, icon, Guard(onInvoke, withdrawn), Guard(canExecute, withdrawn));
    }

    // Each handler is wrapped so a withdrawn add-in stops hearing events, including ones it subscribed after Initialize, which
    // nothing recorded to take out. Unsubscribing finds the wrapper the handler was given.
    private sealed class OwnedEvents(IAddinEvents events, Func<bool> withdrawn) : IAddinEvents
    {
        private readonly Dictionary<Delegate, List<Delegate>> _wrappers = [];

        private T Wrap<T>(T handler, T wrapper) where T : Delegate
        {
            lock (_wrappers)
            {
                if (!_wrappers.TryGetValue(handler, out var list)) _wrappers[handler] = list = [];
                list.Add(wrapper);
            }
            return wrapper;
        }

        private T? Unwrap<T>(T? handler) where T : Delegate
        {
            if (handler is null) return null;
            lock (_wrappers)
            {
                if (!_wrappers.TryGetValue(handler, out var list) || list.Count == 0) return null;
                var wrapper = list[^1];
                list.RemoveAt(list.Count - 1);
                if (list.Count == 0) _wrappers.Remove(handler);
                return (T)wrapper;
            }
        }

        public event Action<AddinProjectEventArgs>? ProjectLoaded
        {
            add { if (value is not null) events.ProjectLoaded += Wrap(value, args => { if (!withdrawn()) value(args); }); }
            remove => events.ProjectLoaded -= Unwrap(value);
        }
        public event Action<AddinProjectEventArgs>? ProjectUnloaded
        {
            add { if (value is not null) events.ProjectUnloaded += Wrap(value, args => { if (!withdrawn()) value(args); }); }
            remove => events.ProjectUnloaded -= Unwrap(value);
        }
        public event Action<AddinFileEventArgs>? FileOpened
        {
            add { if (value is not null) events.FileOpened += Wrap(value, args => { if (!withdrawn()) value(args); }); }
            remove => events.FileOpened -= Unwrap(value);
        }
        public event Action<AddinFileEventArgs>? FileClosed
        {
            add { if (value is not null) events.FileClosed += Wrap(value, args => { if (!withdrawn()) value(args); }); }
            remove => events.FileClosed -= Unwrap(value);
        }
        public event Action? RunStarted
        {
            add { if (value is not null) events.RunStarted += Wrap(value, () => { if (!withdrawn()) value(); }); }
            remove => events.RunStarted -= Unwrap(value);
        }
        public event Action? RunStopped
        {
            add { if (value is not null) events.RunStopped += Wrap(value, () => { if (!withdrawn()) value(); }); }
            remove => events.RunStopped -= Unwrap(value);
        }
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
    public IDisposable RegisterDesignPositions(IDesignPositions positions) =>
        host is HexIdeHost concrete ? concrete.RegisterDesignPositions(positions, origin) : host.RegisterDesignPositions(positions);
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
