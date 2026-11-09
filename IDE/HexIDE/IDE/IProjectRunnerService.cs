using System.ComponentModel;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public interface IProjectRunnerService : INotifyPropertyChanged
{
    /// <summary>A new run has been claimed, before interpreter code can execute; also raised when an external session starts.</summary>
    event Action? RunStarting;

    void RunProject(ProjectDefinition projectDefinition, bool stepInto = false);
    /// <summary>Runs the startup project in the IDE, as Run To Cursor, Restart, a full compile and F8 from idle do.</summary>
    void RunStartupProject(bool stepInto = false);

    /// <summary>
    /// What F5 and Run &gt; Start do from idle: the startup project with the add-in launch its user chose for it, or in
    /// the IDE when there is none. A chosen launch that is gone, or no longer runs the project, leaves it to the IDE, and
    /// the status bar says so.
    /// </summary>
    void StartDefaultProject();

    /// <summary>
    /// Runs a project under the External Debug Session with a launch an add-in contributed, once, whatever F5 runs for
    /// the project. Does nothing while something is running.
    /// </summary>
    void StartContributedLaunch(HexIDE.Debugging.ContributedDebugLaunch launch, ProjectDefinition project);

    void BreakCurrentProject();
    void ContinueProject();
    void StepIntoProject();
    void StepOverProject();
    void StepOutProject();
    void RunToCursorProject(DocumentIdentity document, int line);
    void EndProject();
    void RestartProject();
    /// <summary>
    /// Whether F5 can start the startup project: one HexIDE read itself, or one an add-in read whose user chose a launch
    /// for it that is registered and runs it.
    /// </summary>
    bool CanStartDefaultProject { get; }
    /// <summary>Whether the IDE's own interpreter can run the startup project (<see cref="RunStartupProject"/>): one HexIDE read itself.</summary>
    bool CanRunStartupProject { get; }
    bool CanStartDefaultProjectWithFullCompile { get; }
    bool CanBreakProject { get; }
    bool CanContinueProject { get; }
    bool CanStepIntoProject { get; }
    bool CanStepOverProject { get; }
    bool CanStepOutProject { get; }
    bool CanRunToCursor { get; }
    bool CanEndProject { get; }
    bool CanRestartProject { get; }
    bool IsRunning { get; }

    /// <summary>
    /// The startup project, when nothing is running and no run engine runs it, with the launches add-ins offer for it; null
    /// otherwise. It is what a tool that needs an engine has to explain (#3514). Each launch is asked whether it runs the
    /// project every time this is read, so read it on the UI thread.
    /// </summary>
    NoEngineStartup? StartupWithNoEngine { get; }

    /// <summary>The running snapshot and documents edited since start, for automation replies.</summary>
    string? CodeEditNote(bool edit = false);

    /// <summary>
    /// A start that never became a run: a module the run loads has a syntax error (the text names the module), the
    /// project has no startup form, a Sub Main project has no usable Main or two of them, the startup form could not
    /// be built, or a Sub Main project's code could not be loaded. Raised synchronously, before the start call
    /// returns, with the text to show.
    /// Nothing is running afterwards.
    /// </summary>
    event Action<string>? StartFailed;

    /// <summary>
    /// A run that did start and was then ended by an error, with the text to show. Either Sub Main stopped on
    /// a compile or run-time error it did not handle, or the program hit something that is not a VB6 error
    /// at all: a construct HexIDE does not implement, or a fault in the engine (#754). Raised after the run
    /// has been torn down, so nothing is running when it arrives. A form's ordinary VB6 run-time error is
    /// not reported here: it leaves the run going, as VB6's does, until End is pressed.
    /// </summary>
    event Action<string>? RunFailed;

    /// <summary>
    /// A running program met an error and carries on: an event procedure was abandoned and the form is still there, until
    /// End is pressed. The failure gives the text to show and where the error was raised.
    /// </summary>
    event Action<RunFailure>? ErrorMet;

    /// <summary>
    /// Who answers an error nothing in the program handles, while the program waits at the statement that raised it
    /// (#1011). It answers true for Debug, and false for End once it has ended the run.
    /// </summary>
    Func<RunFailure, Task<bool>>? HaltHost { get; set; }

    /// <summary>
    /// Where the error that <see cref="StartFailed"/> or <see cref="RunFailed"/> is reporting was raised, for a handler to
    /// read while it runs; null at any other time, and when no statement raised it, as for a syntax error found before
    /// the run began (#955).
    /// </summary>
    HexIDE.Runtime.Interpreter.ErrorLocation? FailureLocation => null;

    /// <summary>The project currently running, or null when nothing is.</summary>
    ProjectDefinition? RunningProject { get; }
}

/// <summary>A startup project no run engine runs, and what can run it instead.</summary>
/// <param name="Project">The project.</param>
/// <param name="Launches">The launches add-ins offer for it, in the order the Run menu lists them. Empty when none does.</param>
public sealed record NoEngineStartup(ProjectDefinition Project, IReadOnlyList<HexIDE.Debugging.ContributedDebugLaunch> Launches);
