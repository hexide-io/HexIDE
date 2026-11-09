using Avalonia.Headless.XUnit;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
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
    private readonly IDebugGate _debugger = Substitute.For<IDebugGate>();
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

    [AvaloniaFact]
    public void An_error_the_engine_meets_reaches_whoever_listens_to_the_runner()
    {
        var met = new List<RunFailure>();
        var failure = new RunFailure("Run-time error '5'", new ErrorLocation("Form1", 3));
        _runner.ErrorMet += met.Add;

        _engine.Meets(failure);
        _runner.ErrorMet -= met.Add;
        _engine.Meets(failure);

        met.Should().Equal(failure);
    }

    [AvaloniaFact]
    public void The_engine_asks_the_host_the_runner_was_given()
    {
        Func<RunFailure, Task<bool>> host = _ => Task.FromResult(true);

        _runner.HaltHost = host;

        _engine.HaltHost.Should().BeSameAs(host);
        _runner.HaltHost.Should().BeSameAs(host);
    }

    [AvaloniaFact]
    public void The_interpreter_halts_only_while_it_has_a_host_to_ask()
    {
        var controller = new DebugController();
        var engine = new InterpreterRunEngine(Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(), controller);

        controller.CanHalt.Should().BeFalse("with nobody to ask, the error unwinds to be reported");
        engine.HaltHost = _ => Task.FromResult(true);
        controller.CanHalt.Should().BeTrue();
        engine.HaltHost = null;
        controller.CanHalt.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task The_interpreter_asks_its_host_in_the_words_of_the_dialog_and_passes_on_the_answer()
    {
        var controller = new DebugController();
        var engine = new InterpreterRunEngine(Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(), controller);
        var asked = new List<RunFailure>();
        engine.HaltHost = failure =>
        {
            asked.Add(failure);
            return Task.FromResult(false);
        };
        var error = await RaisedBy("Error \"x\"\n");
        ErrorLocation.Note(error, "Module1", 1);

        var answer = await controller.HaltHost!(error);

        answer.Should().BeFalse();
        asked.Should().Equal(new RunFailure("Run-time error '13':\n\nType mismatch\n\nat Error \"x\"", new ErrorLocation("Module1", 1)));
    }

    [AvaloniaFact]
    public void A_run_time_error_that_leaves_the_run_going_is_passed_on_with_its_statement_and_place()
    {
        var met = ErrorsMetRunning("    Error \"x\"\n");

        met.Should().Equal(new RunFailure("Run-time error '13':\n\nType mismatch\n\nat Error \"x\"", new ErrorLocation("Form1", 2)));
    }

    [AvaloniaFact]
    public void A_compile_error_met_while_a_form_runs_is_passed_on_as_a_run_time_error_is()
    {
        // A call to a Sub that does not exist, reached from a click: the compile error a tree-walker meets mid-run.
        var met = ErrorsMetRunning("    NoSuchSub\n");

        met.Should().ContainSingle();
        met[0].Text.Should().Contain("Sub or Function not defined");
        met[0].Location.Should().Be(new ErrorLocation("Form1", 2));
    }

    private static List<RunFailure> ErrorsMetRunning(string body)
    {
        var engine = new InterpreterRunEngine(Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(), new DebugController());
        var met = new List<RunFailure>();
        engine.ErrorMet += met.Add;
        var form = new VBWindowContext(Substitute.For<IBasicStandardLibrary>());
        form.SetCode($"Private Sub Command1_Click()\n{body}End Sub\n", "Form1");

        form.ExecuteSub("Command1_Click");

        return met;
    }

    private static async Task<VBRunTimeException> RaisedBy(string code)
    {
        var interpreter = new BasicInterpreter(Substitute.For<IBasicStandardLibrary>(), new ModuleExecutionContext(), new ExecutionEnvironment(), code);
        var act = async () => await interpreter.Execute();
        return (await act.Should().ThrowAsync<VBRunTimeException>()).Which;
    }

    private sealed class FakeEngine : IRunEngine
    {
        public event Action<RunFailure>? ErrorMet;

        public Func<RunFailure, Task<bool>>? HaltHost { get; set; }

        public void Meets(RunFailure failure) => ErrorMet?.Invoke(failure);

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
