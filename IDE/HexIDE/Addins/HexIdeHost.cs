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
    IComponentRegistry componentRegistry,
    IProjectRunnerService projectRunner,
    HexIDE.Lsp.LanguageServerContributions languageServers,
    HexIDE.Debugging.DebugLaunchService debugLaunches,
    HexIDE.Projects.ContributedProjects contributedProjects) : IHexIdeHost
{
    public IMenuContributor Menus { get; } = menuService;
    public IToolWindowContributor ToolWindows { get; } = toolWindowService;
    public IAddinEvents Events { get; } = eventService;
    public IAddinRegistry Registry { get; } = registry;
    public IEditorAccess Editor { get; } = editorService;
    public IProjectAccess Project { get; } = projectService;
    public IDiagnosticsAccess Diagnostics { get; } = diagnosticsService;
    public IOptionsPageContributor Options { get; } = optionsService;

    public IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        commandService.Register(id, label, onInvoke, gesture, canExecute);

    public IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        RegisterCommand(id, label, onInvoke, gesture, canExecute, default);

    internal IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture, Func<bool>? canExecute, AddinOrigin origin) =>
        commandService.RegisterOwned(origin, id, label, onInvoke, gesture, canExecute);

    public IDisposable RegisterComponent(IComponentClass component) =>
        componentRegistry.Register(component);

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
                    addins.CheckLanguageServerLaunch(origin.Id);
                    if (gate is not null) await gate(token);
                    addins.CheckLanguageServerLaunch(origin.Id);
                },
            };
        }
        var connection = languageServers.Register(server, origin.Title);
        if (Registry is AddinRegistry owner) owner.TrackContribution(origin.Id, connection);
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
        var handle = debugLaunches.Register(launch, origin, addins is null ? null : () => addins.CheckDebugLaunch(origin.Id));
        addins?.TrackContribution(origin.Id, handle);
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
        var handle = contributedProjects.Register(type, origin);
        if (Registry is AddinRegistry addins) addins.TrackContribution(origin.Id, handle);
        return handle;
    }
}
