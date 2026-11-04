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

    /// <summary>
    /// Contributes a kind of project file HexIDE then opens, from Open Project and the recent projects list, as a project
    /// whose membership is the add-in's program's to change (<see cref="AddinProjectType"/>).
    /// </summary>
    /// <remarks>
    /// The capability design will need a name for this member; none is checked today, as that design's host checks are not
    /// built.
    /// </remarks>
    /// <returns>What takes the type out again: its files are no longer offered, and a project of it already open stays.</returns>
    /// <exception cref="ArgumentException">It has no title, no file names, a malformed pattern, or nothing that reads it.</exception>
    /// <exception cref="InvalidOperationException">Another project type, an add-in's or HexIDE's own, reads one of its file names.</exception>
    IDisposable RegisterProjectType(AddinProjectType type);
}
