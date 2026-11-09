using Avalonia.Headless.XUnit;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.DebugAdapter;

/// <summary>
/// What the project runner does around a run engine, with an engine that isn't the interpreter (#3412). The interpreter's
/// own runs are the Integration tests that start one.
/// </summary>
public class RunEngineSeamTests
{
    private readonly ProjectDefinition _project = TestHelpers.CreateProject("Invoices");
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly IDebugController _debugger = Substitute.For<IDebugController>();
    private readonly FakeEngine _engine = new();
    private readonly ProjectRunnerService _runner;
    private readonly List<string> _startFailures = [];
    private readonly List<string> _runFailures = [];
    private int _runStarts;

    public RunEngineSeamTests()
    {
        _projects.StartupProject.Returns(_project);
        _runner = new ProjectRunnerService(Substitute.For<IEventBus>(), _projects, Substitute.For<ILocalizationService>(), _debugger,
            Substitute.For<IBreakpointService>(), new WatchService(), new RunScope(), _engine);
        _runner.StartFailed += _startFailures.Add;
        _runner.RunFailed += _runFailures.Add;
        _runner.RunStarting += () => _runStarts++;
    }

    [AvaloniaFact]
    public void A_project_the_engine_does_not_run_is_not_started_and_F5_cannot_start_it()
    {
        _engine.RunsIt = false;

        _runner.CanRunStartupProject.Should().BeFalse();
        _runner.CanStartDefaultProject.Should().BeFalse();
        _runner.RunStartupProject();

        _engine.Prepared.Should().Be(0);
        _startFailures.Should().BeEmpty();
        _runStarts.Should().Be(0);
    }

    [AvaloniaFact]
    public void F5_can_start_a_project_the_engine_runs()
    {
        _runner.CanRunStartupProject.Should().BeTrue();
        _runner.CanStartDefaultProject.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_refusal_is_reported_before_anything_is_claimed()
    {
        _engine.Refusal = "Nothing to start";

        _runner.RunStartupProject();

        _startFailures.Should().Equal("Nothing to start");
        _engine.Prepared.Should().Be(0);
        _runStarts.Should().Be(0);
        _debugger.DidNotReceive().Reset();
    }

    [AvaloniaFact]
    public void The_run_is_claimed_and_the_debugger_set_up_before_the_engine_prepares_it()
    {
        ProjectDefinition? runningWhenPrepared = null;
        _engine.OnPrepare = () =>
        {
            runningWhenPrepared = _runner.RunningProject;
            _debugger.Received(1).Reset();
        };

        _runner.RunStartupProject();

        runningWhenPrepared.Should().BeSameAs(_project);
        _runStarts.Should().Be(1);
    }

    [AvaloniaFact]
    public void A_failed_start_is_torn_down_then_reported_with_its_location_before_the_start_call_returns()
    {
        var at = new ErrorLocation("Module1", 7);
        _engine.StartFailure = new RunFailure("Syntax error", at);
        ErrorLocation? seen = null;
        var runningInHandler = true;
        _runner.StartFailed += _ =>
        {
            seen = _runner.FailureLocation;
            runningInHandler = _runner.IsRunning || _runner.RunningProject is not null;
            _debugger.Received().Stop();
        };

        _runner.RunStartupProject();

        _startFailures.Should().Equal("Syntax error");
        seen.Should().Be(at);
        runningInHandler.Should().BeFalse();
        _runner.FailureLocation.Should().BeNull("it is read only while the failure is reported");
        _runner.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void The_run_can_be_ended_from_its_first_statement()
    {
        var runningWhenStarted = false;
        var endableWhenStarted = false;
        _engine.Run.OnRun = () =>
        {
            runningWhenStarted = _runner.IsRunning;
            endableWhenStarted = _runner.CanEndProject;
        };

        _runner.RunStartupProject();

        runningWhenStarted.Should().BeTrue("a Sub Main run has its handle before its first statement");
        endableWhenStarted.Should().BeTrue();
    }

    [AvaloniaFact]
    public void End_stops_the_debugger_first_and_then_ends_the_program()
    {
        var stoppedBeforeEnd = false;
        _engine.Run.OnEnd = () => stoppedBeforeEnd = _debugger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IDebugController.Stop));
        _runner.RunStartupProject();

        _runner.EndProject();

        _engine.Run.Ends.Should().Be(1);
        stoppedBeforeEnd.Should().BeTrue("Stop releases the break gate that ending alone cannot");
        _runner.IsRunning.Should().BeFalse();
        _runner.RunningProject.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_clean_end_tears_the_run_down_and_reports_nothing()
    {
        _runner.RunStartupProject();
        _runner.IsRunning.Should().BeTrue();

        _engine.Run.Ended.SetResult(null);

        _runner.IsRunning.Should().BeFalse();
        _runner.RunningProject.Should().BeNull();
        _debugger.Received().Stop();
        _runFailures.Should().BeEmpty();
        _startFailures.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_failure_is_reported_once_the_run_is_torn_down()
    {
        var at = new ErrorLocation("Module1", 12);
        ErrorLocation? seen = null;
        var runningInHandler = true;
        _runner.RunFailed += _ =>
        {
            seen = _runner.FailureLocation;
            runningInHandler = _runner.IsRunning || _runner.RunningProject is not null;
        };
        _runner.RunStartupProject();

        _engine.Run.Ended.SetResult(new RunFailure("Run-time error '5'", at));

        _runFailures.Should().Equal("Run-time error '5'");
        seen.Should().Be(at);
        runningInHandler.Should().BeFalse("the dialog never appears beside a run that still looks live");
        _runner.FailureLocation.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_project_an_add_in_read_is_not_the_interpreters_to_run()
    {
        var engine = new InterpreterRunEngine(Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(), _debugger);
        var contributed = new ProjectDefinition(VBProjectType.EXE, "Theirs") { ContributedBy = "com.example.addin" };

        engine.Runs(_project).Should().BeTrue();
        engine.Runs(contributed).Should().BeFalse();
    }

    [AvaloniaFact]
    public void The_interpreter_refuses_a_project_with_no_startup_object_before_it_is_claimed()
    {
        var engine = new InterpreterRunEngine(Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(), _debugger);

        engine.RefusesToStart(_project).Should().NotBeNullOrEmpty("the test project has no startup form");
    }

    private sealed class FakeEngine : IRunEngine
    {
        public bool RunsIt { get; set; } = true;
        public string? Refusal { get; set; }
        public RunFailure? StartFailure { get; set; }
        public Action? OnPrepare { get; set; }
        public FakeRun Run { get; } = new();
        public int Prepared { get; private set; }

        public bool Runs(ProjectDefinition project) => RunsIt;

        public string? RefusesToStart(ProjectDefinition project) => Refusal;

        public RunStart Prepare(ProjectDefinition project)
        {
            Prepared++;
            OnPrepare?.Invoke();
            return StartFailure is { } failure ? RunStart.Failed(failure) : RunStart.Started(Run);
        }
    }

    private sealed class FakeRun : IEngineRun
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
}
