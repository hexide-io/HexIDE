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

    /// <summary>The project currently running, or null when nothing is.</summary>
    ProjectDefinition? RunningProject { get; }
}