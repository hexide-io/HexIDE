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
    IProjectRunnerService projectRunner) : IHexIdeHost
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
}
