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
    /// Contributes a language server (#457, #2760): one the IDE launches, routes documents to and lists in the Language
    /// Servers window as it does one of <c>lsp-servers.json</c>, under the add-in's name. It starts on the first document
    /// it claims; one that is open already starts it now.
    /// </summary>
    /// <remarks>
    /// The capability design maps this member to <c>languageServer.contribute</c>. It checks no declaration today: that
    /// design's host checks are not built, so any add-in can call it.
    /// </remarks>
    /// <returns>The connection the add-in's own messages go over. Disposing it stops the server and takes it out.</returns>
    /// <exception cref="ArgumentException">
    /// The entry fails the checks an entry of the file gets, or its message prefix or one of its methods is not the
    /// add-in's to use. What was wrong with the entry is reported in the Language Servers window too.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The id is taken: the configuration names it, and keeps its own entry, or another add-in contributed it first.
    /// </exception>
    IAddinServerConnection RegisterLanguageServer(AddinLanguageServer server);

    /// <summary>
    /// Contributes a way to run a project under the External Debug Session: it appears under Run &gt; Start Once With and
    /// as a choice under Run &gt; F5 Runs, and F5 runs it only for a project whose user chose it there.
    /// </summary>
    /// <remarks>
    /// The capability design will need a name for this member, separate from <c>program.run</c>; none is checked today,
    /// as that design's host checks are not built.
    /// </remarks>
    /// <returns>
    /// What takes the launch out again: its command goes, and F5 no longer offers it. Turning the add-in off or revoking its
    /// consent takes it out too, and a run already starting is refused before its adapter is started.
    /// </returns>
    /// <exception cref="ArgumentException">It has no title, or nothing makes its configuration.</exception>
    /// <exception cref="InvalidOperationException">This add-in contributed a launch with that title already.</exception>
    IDisposable RegisterDebugLaunch(AddinDebugLaunch launch);

    /// <summary>
    /// Contributes a kind of project file HexIDE then opens, from Open Project, the recent projects list or a project group,
    /// as a project whose membership is the add-in's program's to change (<see cref="AddinProjectType"/>).
    /// </summary>
    /// <remarks>
    /// The capability design will need a name for this member; none is checked today, as that design's host checks are not
    /// built.
    /// </remarks>
    /// <returns>
    /// What takes the type out again: its files are no longer offered, and a project of it already open stays. Turning the
    /// add-in off or revoking its consent takes it out too.
    /// </returns>
    /// <exception cref="ArgumentException">It has no title, no file names, a malformed pattern, or nothing that reads it.</exception>
    /// <exception cref="InvalidOperationException">Another project type, an add-in's or HexIDE's own, reads one of its file names.</exception>
    IDisposable RegisterProjectType(AddinProjectType type);
}
