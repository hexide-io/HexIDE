using System.ComponentModel;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public interface IProjectRunnerService : INotifyPropertyChanged
{
    void RunProject(ProjectDefinition projectDefinition, bool stepInto = false);
    void RunStartupProject(bool stepInto = false);
    void BreakCurrentProject();
    void ContinueProject();
    void StepIntoProject();
    void StepOverProject();
    void StepOutProject();
    void RunToCursorProject(DocumentIdentity document, int line);
    void EndProject();
    void RestartProject();
    bool CanStartDefaultProject { get; }
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

    /// <summary>The project currently running, or null when nothing is.</summary>
    ProjectDefinition? RunningProject { get; }
}