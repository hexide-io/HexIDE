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
        var connection = languageServers.Register(server, OriginOf(System.Reflection.Assembly.GetCallingAssembly()));
        AddinInitializationScope.Record(connection.Dispose);
        return connection;
    }

    // The title of the add-in an assembly is, as the add-in manager shows it; the assembly's name when no add-in is it.
    private string OriginOf(System.Reflection.Assembly assembly)
    {
        var path = FullPath(assembly.Location);
        return (path.Length == 0 ? null : Registry.GetAll().FirstOrDefault(addin =>
                   string.Equals(FullPath(addin.AssemblyPath), path, StringComparison.OrdinalIgnoreCase))?.Title)
               ?? assembly.GetName().Name
               ?? "An add-in";

        static string FullPath(string path)
        {
            try { return string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetFullPath(path); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException) { return path; }
        }
    }
}
