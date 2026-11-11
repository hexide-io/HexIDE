using System;
using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// What runs a project inside the IDE when F5 starts it. The project runner claims the run, sets up and tears down the
/// debugger, and reports what happens; an engine builds the program and runs it. Add-ins' launches are the other way a
/// project runs: they go through the External Debug Session and never reach an engine.
/// </summary>
/// <remarks>The three methods are called on the UI thread, in the order given here, once for each start.</remarks>
public interface IRunEngine
{
    /// <summary>Whether this engine runs this project. When it does not, no start reaches the engine.</summary>
    bool Runs(ProjectDefinition project);

    /// <summary>
    /// Why this project cannot be started, in the words to show, or null when it can. Asked before the run is claimed, so
    /// a refusal leaves nothing to tear down: a project with no startup object is the case that needs this.
    /// </summary>
    string? RefusesToStart(ProjectDefinition project);

    /// <summary>
    /// Builds the program, after the run has been claimed and the debugger set up. A failure here is a start that never
    /// became a run, and comes back as <see cref="RunStart.Failed"/> rather than being thrown.
    /// </summary>
    /// <remarks>May begin the program, as a form's Load does, but must not wait for it to end.</remarks>
    RunStart Prepare(ProjectDefinition project);

    /// <summary>
    /// A running program met an error and carries on: an event procedure was abandoned and the form is still there. The
    /// host records it and shows it.
    /// </summary>
    event Action<RunFailure>? ErrorMet;

    /// <summary>
    /// Who answers an error nothing in the program handles, at the statement that raised it and before anything unwinds
    /// (#1011). It answers true for Debug, and false for End once it has ended the run. While it is null the program does
    /// not halt, and the error unwinds to be reported by <see cref="ErrorMet"/> or as what ended the run.
    /// </summary>
    Func<RunFailure, Task<bool>>? HaltHost { get; set; }
}

/// <summary>A program an engine has built.</summary>
public interface IEngineRun
{
    /// <summary>
    /// Runs the program until it has ended by any route, answering with the failure that ended it, or null for a clean
    /// end. Called once, after the run can be ended, so that <see cref="End"/> works from the program's first statement.
    /// </summary>
    Task<RunFailure?> RunToEndAsync();

    /// <summary>Ends the program, closing its windows, whatever state it is in. Harmless once it has ended.</summary>
    void End();
}

/// <summary>What ended a run or stopped it starting.</summary>
/// <param name="Text">The words to show.</param>
/// <param name="Location">Where the error was raised, or null when no statement raised it.</param>
public sealed record RunFailure(string Text, ErrorLocation? Location);

/// <summary>What <see cref="IRunEngine.Prepare"/> answers: a program ready to run, or why there is none.</summary>
public sealed class RunStart
{
    private RunStart(IEngineRun? run, RunFailure? failure)
    {
        Run = run;
        Failure = failure;
    }

    /// <summary>The program, or null when the start failed.</summary>
    public IEngineRun? Run { get; }

    /// <summary>Why the start failed, or null when it did not.</summary>
    public RunFailure? Failure { get; }

    /// <summary>The start worked.</summary>
    public static RunStart Started(IEngineRun run) => new(run, null);

    /// <summary>The start never became a run.</summary>
    public static RunStart Failed(RunFailure failure) => new(null, failure);
}
