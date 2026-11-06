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
    HexIDE.Lsp.LanguageServerContributions languageServers) : IHexIdeHost
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

    public void RegisterComponent(IComponentClass component) =>
        componentRegistry.Register(component);

    public void RegisterProjectTemplate(IAddinProjectTemplate template) =>
        projectTemplateService.Register(template);

    public bool RunProject() => Dispatcher.UIThread.Invoke(() =>
    {
        if (!projectRunner.CanStartDefaultProject) return false;
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
        if (Registry is AddinRegistry owner) owner.TrackLanguageServer(origin.Id, connection);
        AddinInitializationScope.Record(connection.Dispose);
        return connection;
    }
}
