using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// A run engine that runs nothing, for a test about what the IDE decides around a run rather than what a program does
/// (#3663). Each answer is set by the test, and it counts what it was asked, so a test can say the engine was or wasn't
/// reached. A test that needs a program to run uses <see cref="TestRunners.WithInterpreter"/> instead.
/// </summary>
internal sealed class StandInRunEngine : IRunEngine
{
    public event Action<RunFailure>? ErrorMet;

    public Func<RunFailure, Task<bool>>? HaltHost { get; set; }

    /// <summary>Raises <see cref="ErrorMet"/>, as an engine does when a running program meets an error.</summary>
    public void Meets(RunFailure failure) => ErrorMet?.Invoke(failure);

    /// <summary>
    /// What <see cref="Runs"/> answers. Null by default, and then it runs what the project's type offers to run, as an
    /// engine does: an add-in's read-only project goes to a launch rather than to the engine.
    /// </summary>
    public bool? RunsIt { get; set; }

    /// <summary>What <see cref="RefusesToStart"/> answers. Null by default, which starts.</summary>
    public string? Refusal { get; set; }

    /// <summary>The failure <see cref="Prepare"/> reports, or null for a start that becomes <see cref="Run"/>.</summary>
    public RunFailure? StartFailure { get; set; }

    public Action? OnPrepare { get; set; }

    /// <summary>The run every successful <see cref="Prepare"/> returns.</summary>
    public StandInRun Run { get; } = new();

    /// <summary>How many times the engine was asked to prepare a run.</summary>
    public int Prepared { get; private set; }

    public bool Runs(ProjectDefinition project) => RunsIt ?? project.Offers.Runs;

    public string? RefusesToStart(ProjectDefinition project) => Refusal;

    public RunStart Prepare(ProjectDefinition project)
    {
        Prepared++;
        OnPrepare?.Invoke();
        return StartFailure is { } failure ? RunStart.Failed(failure) : RunStart.Started(Run);
    }
}

/// <summary>The run a <see cref="StandInRunEngine"/> starts: it ends when the test says so.</summary>
internal sealed class StandInRun : IEngineRun
{
    public TaskCompletionSource<RunFailure?> Ended { get; } = new();
    public Action? OnRun { get; set; }
    public Action? OnEnd { get; set; }
    public int Ends { get; private set; }

    public Task<RunFailure?> RunToEndAsync()
    {
        OnRun?.Invoke();
        return Ended.Task;
    }

    public void End()
    {
        Ends++;
        OnEnd?.Invoke();
    }
}
