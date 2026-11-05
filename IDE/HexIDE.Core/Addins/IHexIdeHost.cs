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
    /// Contributes a way to run a project under the External Debug Session: it appears under Run &gt; Start Once With and
    /// as a choice under Run &gt; F5 Runs, and F5 runs it only for a project whose user chose it there.
    /// </summary>
    /// <remarks>
    /// The capability design will need a name for this member, separate from <c>program.run</c>; none is checked today,
    /// as that design's host checks are not built.
    /// </remarks>
    /// <returns>What takes the launch out again: its command goes, and F5 no longer offers it.</returns>
    /// <exception cref="ArgumentException">It has no title, or nothing makes its configuration.</exception>
    /// <exception cref="InvalidOperationException">This add-in contributed a launch with that title already.</exception>
    IDisposable RegisterDebugLaunch(AddinDebugLaunch launch);
}
