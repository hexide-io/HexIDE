using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Debugging;

/// <summary>
/// Which project the current run belongs to.
///
/// <para>
/// The interpreter's debug controller reports a pause by bare module name, and rightly so: a run is one
/// project, and its gate knows nothing of the IDE's documents. But a code editor receiving that name has to
/// decide whether the pause is in <em>its</em> document, and with two projects of a group each holding a
/// <c>Module1</c> the name alone cannot answer. So the editor asks this which project is running, and matches
/// on the document's identity rather than on its name.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// A service of its own rather than a property on the project runner, and a value rather than an event, for
/// two reasons that both come from where it is read.
/// </para>
/// <para>
/// An editor cannot depend on the runner: the runner is reached through the editor factory, so the
/// dependency would close a cycle. It is the same reason the reset prompt is routed through the event bus.
/// </para>
/// <para>
/// And an event alone would not do, because the editor that most needs this is the one opened <em>by</em> a
/// break — the run started before that view model existed, so it never saw the notification. A value can be
/// asked at the moment it matters; a notification can only be heard while you are listening.
/// </para>
/// </remarks>
public interface IRunScope
{
    /// <summary>The project currently running, or null when nothing is.</summary>
    ProjectDefinition? RunningProject { get; }
}

/// <inheritdoc cref="IRunScope"/>
public sealed class RunScope : IRunScope
{
    /// <summary>Set by the project runner as a run starts and ends. Nothing else writes it.</summary>
    public ProjectDefinition? RunningProject { get; set; }
}
