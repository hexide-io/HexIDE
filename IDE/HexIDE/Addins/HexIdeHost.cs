using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime.Components;

namespace HexIDE.Addins;

internal sealed class HexIdeHost(
    AddinMenuService menuService,
    AddinToolWindowService toolWindowService,
    AddinEventService eventService,
    IAddinRegistry registry,
    AddinEditorService editorService,
    AddinProjectService projectService,
    AddinDiagnosticsService diagnosticsService,
    AddinCommandService commandService,
    AddinProjectTemplateService projectTemplateService,
    AddinOptionsService optionsService,
    ComponentRegistry componentRegistry,
    IProjectRunnerService projectRunner,
    HexIDE.Lsp.LanguageServerContributions languageServers,
    HexIDE.Debugging.DebugLaunchService debugLaunches,
    HexIDE.Projects.ContributedProjects contributedProjects,
    AddinResourceService resources,
    AddinLanguagePackService languagePacks,
    AddinLanguageEditingService languageEditing, AddinRunEngineService? runEngines = null) : IHexIdeHost,
    HexIDE.Language.ILanguageEditingRegistration, IRunEngineRegistration
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void ReportFailure(string message, Exception? exception = null) =>
        ReportFailure(message, exception, AddinOrigin.Of(System.Reflection.Assembly.GetCallingAssembly(), Registry));

    internal void ReportFailure(string message, Exception? exception, AddinOrigin origin)
    {
        if (Registry is AddinRegistry addins) addins.ReportFailure(origin, message, exception);
    }

    public IMenuContributor Menus { get; } = menuService;
    public IToolWindowContributor ToolWindows { get; } = toolWindowService;
    public IAddinEvents Events { get; } = eventService;
    public IAddinRegistry Registry { get; } = registry;
    public IEditorAccess Editor { get; } = editorService;
    public IProjectAccess Project { get; } = projectService;
    public IDiagnosticsAccess Diagnostics { get; } = diagnosticsService;
    public IOptionsPageContributor Options { get; } = optionsService;
    public IServerChatTools ServerChatTools => languageServers.ChatTools;

    public IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        commandService.Register(id, label, onInvoke, gesture, canExecute);

    public IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        RegisterCommand(id, label, onInvoke, gesture, canExecute, default);

    internal IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture, Func<bool>? canExecute, AddinOrigin origin) =>
        commandService.RegisterOwned(origin, id, label, onInvoke, gesture, canExecute);

    // As resources: only an add-in's own host registers a component or a toolbox item, with the add-in as its owner (#5594).
    public IDisposable RegisterComponent(IComponentClass component) =>
        throw new InvalidOperationException("Components are registered only through the host an add-in is given.");

    public IDisposable RegisterToolboxItem(IComponentClass component, string? iconKey = null) =>
        throw new InvalidOperationException("Toolbox items are registered only through the host an add-in is given.");

    public IDisposable RegisterControlLibrary(IControlLibrary library, Func<IReadOnlyList<IComponentClass>> controls) =>
        throw new InvalidOperationException("Control libraries are registered only through the host an add-in is given.");

    // Under the add-in's identity, which the registry checks (#5594). Taken out, as the add-in's other contributions are,
    // when it is disabled or its consent is revoked (#5601): the designer stops offering the class, and the toolbox drops
    // its item, which goes with its class. The composed provider's never meet a package's state (#5597).
    internal IDisposable RegisterComponent(IComponentClass component, AddinOrigin origin)
    {
        var handle = componentRegistry.Register(component, origin);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        return handle;
    }

    // Only the class's owner puts it in the toolbox, and the item goes when the class does, so tracking the class is enough.
    internal IDisposable RegisterToolboxItem(IComponentClass component, string? iconKey, AddinOrigin origin) =>
        componentRegistry.RegisterToolboxItem(component, iconKey, origin);

    // Taken out, as its components are, when the add-in is disabled or its consent is revoked (#5601): the Components dialog
    // stops listing it, and the toolbox stops offering its controls.
    internal IDisposable RegisterControlLibrary(IControlLibrary library, Func<IReadOnlyList<IComponentClass>> controls,
        AddinOrigin origin)
    {
        var handle = componentRegistry.RegisterLibrary(library, controls, origin);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        return handle;
    }

    public IDisposable RegisterProjectTemplate(IAddinProjectTemplate template) =>
        projectTemplateService.Register(template);

    public bool RunProject() => Dispatcher.UIThread.Invoke(() =>
    {
        if (!projectRunner.CanRunStartupProject) return false;
        projectRunner.RunStartupProject();
        return true;
    });

    public bool StopProject() => Dispatcher.UIThread.Invoke(() =>
    {
        if (!projectRunner.CanEndProject) return false;
        projectRunner.EndProject();
        return true;
    });

    // NoInlining keeps this a real frame, so GetCallingAssembly is the add-in's, which names the server's origin.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public IAddinServerConnection RegisterLanguageServer(AddinLanguageServer server)
    {
        var origin = AddinOrigin.Of(System.Reflection.Assembly.GetCallingAssembly(), Registry);
        return RegisterLanguageServer(server, origin);
    }

    internal IAddinServerConnection RegisterLanguageServer(AddinLanguageServer server, AddinOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (Registry is AddinRegistry addins)
        {
            var gate = server.BeforeLaunch;
            server = server with
            {
                BeforeLaunch = async token =>
                {
                    addins.CheckLanguageServerLaunch(origin);
                    if (gate is not null) await gate(token);
                    addins.CheckLanguageServerLaunch(origin);
                },
            };
        }
        var connection = languageServers.Register(server, origin.Title, origin.Id, origin.PublisherNamespaces);
        optionsService.TrackServer(origin.Id, string.IsNullOrWhiteSpace(server.Entry.DisplayName) ? connection.Id : server.Entry.DisplayName, connection,
            origin.IsComposed);
        if (Registry is AddinRegistry owner) owner.TrackContribution(origin, connection);
        AddinInitializationScope.Record(connection.Dispose);
        return connection;
    }

    // NoInlining keeps this a real frame, so GetCallingAssembly is the add-in's, which names the launch's origin.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public IDisposable RegisterDebugLaunch(AddinDebugLaunch launch) =>
        RegisterDebugLaunch(launch, AddinOrigin.Of(System.Reflection.Assembly.GetCallingAssembly(), Registry));

    // A launch starts its adapter only while its add-in may start what it contributed, checked as a server's launch is,
    // and is taken out with the add-in's language servers when it is disabled or its consent is revoked.
    internal IDisposable RegisterDebugLaunch(AddinDebugLaunch launch, AddinOrigin origin)
    {
        var addins = Registry as AddinRegistry;
        var handle = debugLaunches.Register(launch, origin, addins is null ? null : () => addins.CheckDebugLaunch(origin));
        addins?.TrackContribution(origin, handle);
        return handle;
    }

    // NoInlining keeps this a real frame, so GetCallingAssembly is the add-in's, which names the type's origin.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public IDisposable RegisterProjectType(AddinProjectType type) =>
        RegisterProjectType(type, AddinOrigin.Of(System.Reflection.Assembly.GetCallingAssembly(), Registry));

    // Taken out, as its launches are, when the add-in is disabled or its consent is revoked: a project file of the type
    // opened after that is one no loaded add-in reads.
    internal IDisposable RegisterProjectType(AddinProjectType type, AddinOrigin origin)
    {
        // --no-vb6 leaves VB6's type out, as it always has. A no-op, not a refusal: a throw would stop the provider's other
        // registrations (#5625).
        if (HexIDE.Static.WithoutVb6 && contributedProjects.Reserves(type, origin) is { Id: Vb6ProjectType.Id })
        {
            Serilog.Log.Information("Project types: {Owner} ({OwnerId}) registered {Title}, which --no-vb6 leaves out",
                origin.Title, origin.Id, type.Title);
            return new HexIDE.Utils.ActionDisposable(() => { });
        }
        var handle = contributedProjects.Register(type, origin);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        return handle;
    }

    // Only an add-in's own host registers resources, with the add-in as their owner (#5396). This host is the IDE's, so a
    // call that reaches it has no owner to check against or to name in a refusal.
    public IDisposable RegisterResources(Uri dictionary) =>
        throw new InvalidOperationException("Resources are registered only through the host an add-in is given.");

    // Taken out, as the add-in's other contributions are, when it is disabled or its consent is revoked.
    internal IDisposable RegisterResources(Uri dictionary, AddinOrigin origin, System.Reflection.Assembly caller)
    {
        var handle = resources.Register(dictionary, origin, caller);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    // As resources: only an add-in's own host registers language packs, with the add-in as their owner (#5394).
    public IDisposable RegisterLanguagePacks(Uri folder) =>
        throw new InvalidOperationException("Language packs are registered only through the host an add-in is given.");

    // Taken out, as the add-in's other contributions are, when it is disabled or its consent is revoked.
    internal IDisposable RegisterLanguagePacks(Uri folder, AddinOrigin origin, System.Reflection.Assembly caller)
    {
        var handle = languagePacks.Register(folder, origin, caller);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    /// <summary>What draws the designer's controls (#5591). Its registrations are held per application.</summary>
    internal AddinDesignerPictureService DesignerPictures { get; init; } = new();

    // As resources: only an add-in's own host registers what draws the designer's controls, with the add-in as its owner.
    public IDisposable RegisterDesignerPictures(Func<IDesignerDrawingSurface, IDesignerPictures> pictures) =>
        throw new InvalidOperationException("What draws the designer's controls is registered only through the host an add-in is given.");

    // Taken out, as the add-in's other contributions are, when it is disabled or its consent is revoked.
    internal IDisposable RegisterDesignerPictures(Func<IDesignerDrawingSurface, IDesignerPictures> pictures, AddinOrigin origin)
    {
        var handle = DesignerPictures.Register(pictures, origin);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    // As the drawer: only an add-in's own host registers a language's editing helpers, with the add-in as their owner (#5403).
    public IDisposable RegisterLanguageEditing(HexIDE.Language.LanguageEditing editing) =>
        throw new InvalidOperationException("A language's editing helpers are registered only through the host an add-in is given.");

    // Taken out, as the add-in's other contributions are, when it is disabled or its consent is revoked. The withdrawal only
    // takes them out of the table and tells the open code windows, which post their refresh, so it never waits on the UI
    // thread while the add-in registry holds its lock.
    internal IDisposable RegisterLanguageEditing(HexIDE.Language.LanguageEditing editing, AddinOrigin origin)
    {
        var handle = languageEditing.Register(editing, origin);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }

    // As the drawer: only an add-in's own host registers the run engine, with the add-in as its owner (#5616).
    public IDisposable RegisterRunEngine(Func<RunEngineServices, IRunEngine> engine) =>
        throw new InvalidOperationException("The run engine is registered only through the host an add-in is given.");

    // Taken out, as the add-in's other contributions are, when it is disabled or its consent is revoked: the handle posts
    // the withdrawal, so the registry's lock is never held while a program ends.
    internal IDisposable RegisterRunEngine(Func<RunEngineServices, IRunEngine> engine, AddinOrigin origin)
    {
        var engines = runEngines
            ?? throw new InvalidOperationException("This host has no run engines to register with.");
        var handle = engines.Register(engine, origin);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin, handle);
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }
}
