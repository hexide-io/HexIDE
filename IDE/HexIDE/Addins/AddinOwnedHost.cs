using HexIDE.Runtime.Components;

namespace HexIDE.Addins;

// The facade retains its owner even when an add-in registers a window after Initialize returns.
internal sealed class AddinOwnedHost(IHexIdeHost host, IToolWindowContributor tools) : IHexIdeHost
{
    public IToolWindowContributor ToolWindows => tools;
    public IMenuContributor Menus => host.Menus;
    public IAddinEvents Events => host.Events;
    public IAddinRegistry Registry => host.Registry;
    public IEditorAccess Editor => host.Editor;
    public IProjectAccess Project => host.Project;
    public IDiagnosticsAccess Diagnostics => host.Diagnostics;
    public IOptionsPageContributor Options => host.Options;
    public IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
        host.RegisterCommand(id, label, onInvoke, gesture, canExecute);
    public void RegisterComponent(IComponentClass component) => host.RegisterComponent(component);
    public void RegisterProjectTemplate(IAddinProjectTemplate template) => host.RegisterProjectTemplate(template);
    public bool RunProject() => host.RunProject();
    public bool StopProject() => host.StopProject();
}
