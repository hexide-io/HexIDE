using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// What the form designer asks the run engine (#5633): to show a form as it runs, and whether a control the designer only
/// carries is one it runs. Both are facts about the engine, so an engine answers them when it implements this as well as
/// <see cref="IRunEngine"/>; with none that does, the designer previews nothing and every carried control is unsupported.
/// </summary>
/// <remarks>
/// <para>
/// <b>The IDE calls it only through the registered engine,</b> on the UI thread, with the engine's other calls' containment:
/// a throw is logged, and the call answers as with no engine, except that a preview's throw is shown in the box a refused
/// preview is.
/// </para>
/// <para>
/// Public and unbaselined, as <see cref="IRunEngine"/> is: only the composed provider registers an engine in phase 2, through
/// an internal registration, and phase 3 publishes the engine contract with this in it.
/// </para>
/// </remarks>
public interface IDesignerRunEngine
{
    /// <summary>
    /// Shows <paramref name="form"/> as it looks running, in a window of its own over the IDE's, running none of its code:
    /// events have nothing to run them. The engine owns the window, and closes it when it's withdrawn.
    /// </summary>
    /// <returns>Null once it's shown, or why it can't be, which the IDE shows the developer.</returns>
    string? Preview(FormDefinition form);

    /// <summary>
    /// Whether the engine runs a control the designer carries as a block (#2327): of class <paramref name="type"/>, holding
    /// controls of its own or not, and an element of a control array or not.
    /// </summary>
    bool RunsWhenCarried(string type, bool holdsControls, bool inArray);
}
