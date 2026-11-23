using HexIDE.Runtime.Components;

namespace HexIDE.Addins;

/// <summary>
/// Curated facade exposed to add-ins.
/// </summary>
public interface IHexIdeHost
{
    /// <summary>Reports a failure after the add-in has loaded which the user should see in Options → Add-ins.</summary>
    void ReportFailure(string message, Exception? exception = null) { }

    IMenuContributor Menus { get; }
    IToolWindowContributor ToolWindows { get; }
    IAddinEvents Events { get; }
    IAddinRegistry Registry { get; }
    IEditorAccess Editor { get; }
    IProjectAccess Project { get; }
    IDiagnosticsAccess Diagnostics { get; }
    IOptionsPageContributor Options { get; }

    /// <summary>
    /// The tools and context fragments running language servers offer AI Chat (#4257). The host decides which servers
    /// count, each tool's limits and whether a call asks first; the caller asks the person and names the server.
    /// </summary>
    /// <remarks>The capability design would map this to a declaration of its own; none is checked today.</remarks>
    IServerChatTools ServerChatTools { get; }

    IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null);
    IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null);
    /// <summary>Contributes a toolbox control until the returned handle is disposed.</summary>
    IDisposable RegisterComponent(IComponentClass component);
    /// <summary>Contributes a template to subsequently opened New Project dialogs until the returned handle is disposed.</summary>
    IDisposable RegisterProjectTemplate(IAddinProjectTemplate template);

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

    /// <summary>
    /// Adds an Avalonia resource dictionary from the add-in's own assembly to the IDE's resources: the control themes its
    /// controls need, and the resources they look up (#5396).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It only adds.</b> Every key the dictionary holds, in its merged and theme dictionaries too, is read before
    /// anything is merged, and the whole dictionary is refused if any one of them is refused. A key is refused when:
    /// </para>
    /// <list type="bullet">
    /// <item>the IDE or another registration already holds it, in either theme;</item>
    /// <item>it is a string beginning <c>Str.</c>, which is the IDE's text, the consent and trust dialogs' included;</item>
    /// <item>it is a type the add-in's own assembly doesn't declare, which would restyle every control of that type;</item>
    /// <item>it is anything other than a string or a type.</item>
    /// </list>
    /// <para>
    /// So an add-in can give its own controls their themes, and can never change how the IDE, or another add-in, looks
    /// or reads. Each refusal is logged naming the key and both owners.
    /// </para>
    /// <para>
    /// The capability design will need a name for this member; none is checked today, as that design's host checks are
    /// not built.
    /// </para>
    /// </remarks>
    /// <param name="dictionary">An <c>avares://</c> address in the calling add-in's own assembly, whose root is a
    /// <c>ResourceDictionary</c>.</param>
    /// <returns>
    /// What takes the dictionary out again. Turning the add-in off or revoking its consent takes it out too.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The address isn't an <c>avares://</c> address in the calling assembly, or what it names isn't a resource dictionary.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A key is refused, as above, and the message names it and its owner. Also thrown by a host that isn't an add-in's own:
    /// resources are registered only through the host an add-in is given.
    /// </exception>
    IDisposable RegisterResources(Uri dictionary);
}
