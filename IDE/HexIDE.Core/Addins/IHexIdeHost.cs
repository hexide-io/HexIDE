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
}
