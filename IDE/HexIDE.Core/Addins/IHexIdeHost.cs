using HexIDE.Runtime.Components;

namespace HexIDE.Addins;

/// <summary>
/// Curated facade exposed to add-ins.
/// </summary>
public interface IHexIdeHost
{
    IMenuContributor Menus { get; }
    IToolWindowContributor ToolWindows { get; }
    IAddinEvents Events { get; }
    IAddinRegistry Registry { get; }
    IEditorAccess Editor { get; }
    IProjectAccess Project { get; }
    IDiagnosticsAccess Diagnostics { get; }
    IOptionsPageContributor Options { get; }

    IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null);
    void RegisterComponent(IComponentClass component);
    void RegisterProjectTemplate(IAddinProjectTemplate template);

    /// <summary>Starts the startup project in the IDE runtime. Returns false if it cannot start (none loaded or already running).</summary>
    bool RunProject();

    /// <summary>Stops the running project. Returns false if nothing is running.</summary>
    bool StopProject();
}
