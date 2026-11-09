using Avalonia.Headless.XUnit;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.DebugAdapter;

/// <summary>
/// What F5 runs: the interpreter, unless the project's user chose an add-in's launch for it, and the interpreter again,
/// said so, when that launch is gone.
/// </summary>
public class RunWithTests : IAsyncDisposable
{
    private const string Key = "com.rdcore.hexide/RDCore";

    private readonly ConversationLog _log = new();
    private readonly ProjectDefinition _project = TestHelpers.CreateProject("Invoices");
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly RunChoiceStore _choices = new();
    private readonly IStatusBarService _status = Substitute.For<IStatusBarService>();
    private readonly DebugLaunchService _launches;
    private readonly ProjectRunnerService _runner;
    private readonly List<string> _interpreterStarts = [];
    private readonly TaskCompletionSource<AddinProjectInfo> _asked = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RunWithTests()
    {
        var debugger = new ExternalDebugService(_log);
        _launches = new DebugLaunchService(debugger);
        _projects.StartupProject.Returns(_project);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString("Str.Debug.RunWith.Unavailable").Returns("{0} isn't available");
        _runner = TestRunners.WithInterpreter(Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), _projects, strings,
            Substitute.For<IDebugController>(), Substitute.For<IBreakpointService>(), new WatchService(), new RunScope(), debugger,
            debugLaunches: _launches, runChoices: _choices, statusBar: _status);
        // The test project has no startup form, so the interpreter's start fails at once and says so: that is how a
        // test sees that F5 went to the interpreter.
        _runner.StartFailed += _interpreterStarts.Add;
    }

    public async ValueTask DisposeAsync()
    {
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private IDisposable _registrations = new HexIDE.Utils.ActionDisposable(() => { });

    private void Contribute(Func<AddinProjectInfo, bool>? claims = null) =>
        _registrations = _launches.Register(new AddinDebugLaunch("RDCore")
        {
            Claims = claims ?? (_ => true),
            CreateConfigurationAsync = (project, _) =>
            {
                _asked.TrySetResult(project);
                throw new InvalidOperationException("this test stops at the configuration");
            },
        }, "com.rdcore.hexide", "RDCore for HexIDE");

    [AvaloniaFact]
    public void F5_runs_the_interpreter_though_an_add_in_offers_a_launch_until_the_user_chooses_it()
    {
        Contribute();

        _runner.StartDefaultProject();

        _interpreterStarts.Should().ContainSingle();
        _asked.Task.IsCompleted.Should().BeFalse("installing an add-in changes nothing about F5");
    }

    [AvaloniaFact]
    public async Task F5_runs_the_launch_its_user_chose_for_the_project()
    {
        Contribute();
        _choices.Set(_project, Key);

        _runner.StartDefaultProject();

        (await _asked.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)).ProjectName.Should().Be("Invoices");
        _interpreterStarts.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_start_in_the_IDE_by_name_stays_the_interpreter_s_whatever_F5_runs(bool stepInto)
    {
        // RunStartupProject is what Restart, a full compile, Run To Cursor and F8 from idle start with.
        Contribute();
        _choices.Set(_project, Key);

        _runner.RunStartupProject(stepInto);

        _interpreterStarts.Should().ContainSingle();
        _asked.Task.IsCompleted.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Run_To_Cursor_from_idle_stays_the_interpreter_s_whatever_F5_runs()
    {
        Contribute();
        _choices.Set(_project, Key);

        _runner.RunToCursorProject(HexIDE.Runtime.ProjectElements.DocumentIdentity.For(TestHelpers.CreateModule()), 1);

        _interpreterStarts.Should().ContainSingle("the cursor is the interpreter's to run to");
        _asked.Task.IsCompleted.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("gone")]
    [InlineData("no longer claims it")]
    public void A_chosen_launch_that_is_not_there_falls_back_to_the_interpreter_and_says_so(string why)
    {
        if (why != "gone") Contribute(claims: _ => false);
        _choices.Set(_project, Key);

        _runner.StartDefaultProject();

        _interpreterStarts.Should().ContainSingle();
        _status.Received(1).SetTemporaryMessage("RDCore isn't available", Arg.Any<TimeSpan>());
        _choices.Get(_project).Should().Be(Key, "the choice stays, for when the add-in is back");
    }

    [AvaloniaTheory]
    [InlineData("no choice", false)]
    [InlineData("chosen", true)]
    [InlineData("chosen, gone", false)]
    [InlineData("chosen, no longer claims it", false)]
    public void F5_starts_a_project_an_add_in_read_only_with_the_launch_its_user_chose_for_it(string state, bool starts)
    {
        var contributed = new ProjectDefinition(VBProjectType.EXE, "Ledger") { ContributedBy = "RDCore projects" };
        _projects.StartupProject.Returns(contributed);
        if (state != "chosen, gone") Contribute(claims: _ => state != "chosen, no longer claims it");
        if (state != "no choice") _choices.Set(contributed, Key);

        _runner.CanStartDefaultProject.Should().Be(starts);
        _runner.CanRunStartupProject.Should().BeFalse("HexIDE's interpreter runs only the projects it read itself");
        _runner.CanStartDefaultProjectWithFullCompile.Should().BeFalse();
        _runner.CanStepIntoProject.Should().BeFalse("F8 from idle starts the interpreter");
        _runner.CanRunToCursor.Should().BeFalse();
    }

    // A project an add-in read, with a module, as the startup project, its user's choice the launch, which counts the times
    // it is asked whether it runs the project.
    private (ProjectDefinition Project, ModuleDefinition Module) ContributedAndChosen(Func<bool>? answer = null)
    {
        var contributed = new ProjectDefinition(VBProjectType.EXE, "Ledger") { ContributedBy = "RDCore projects" };
        var module = new ModuleDefinition(contributed, "Startup", ModuleKind.StandardModule);
        contributed.AddModule(module);
        _projects.StartupProject.Returns(contributed);
        Contribute(claims: _ =>
        {
            _claimsAsked++;
            return answer?.Invoke() ?? true;
        });
        _choices.Set(contributed, Key);
        return (contributed, module);
    }

    private int _claimsAsked;

    [AvaloniaFact]
    public async Task F5_s_state_asks_the_chosen_launch_once_not_at_each_edit_and_a_start_asks_it_again()
    {
        var (_, module) = ContributedAndChosen();
        _runner.CanStartDefaultProject.Should().BeTrue();
        _claimsAsked.Should().Be(1);

        // Every edit requeries the commands, F5's among them.
        for (var edit = 0; edit < 25; edit++)
        {
            module.UpdateCode($"Sub Main()\r\n    Debug.Print {edit}\r\nEnd Sub\r\n");
            _runner.CanStartDefaultProject.Should().BeTrue();
        }
        _claimsAsked.Should().Be(1, "an edit changes nothing the launch is told of the project");

        _runner.StartDefaultProject();

        _claimsAsked.Should().Be(2, "a start asks the launch, whatever F5's state kept of its answer");
        (await _asked.Task.WaitAsync(TestContext.Current.CancellationToken)).ProjectName.Should().Be("Ledger");
    }

    [AvaloniaFact]
    public void A_start_whose_launch_says_no_now_runs_nothing_of_it_and_F5_s_state_follows()
    {
        var claims = true;
        ContributedAndChosen(() => claims);
        _runner.CanStartDefaultProject.Should().BeTrue();
        claims = false;
        _runner.CanStartDefaultProject.Should().BeTrue("nothing the launch is told of changed, so its answer is kept");

        _runner.StartDefaultProject();

        _asked.Task.IsCompleted.Should().BeFalse("the kept answer does not start a launch that says no");
        _status.Received(1).SetTemporaryMessage("RDCore isn't available", Arg.Any<TimeSpan>());
        _runner.CanStartDefaultProject.Should().BeFalse("F5's state is the start's answer");
        _claimsAsked.Should().Be(2);
    }

    [AvaloniaTheory]
    [InlineData("a module added")]
    [InlineData("a module deleted")]
    [InlineData("a module renamed")]
    [InlineData("a module's file")]
    [InlineData("a form added")]
    [InlineData("a form deleted")]
    [InlineData("a form renamed")]
    [InlineData("a form's file")]
    [InlineData("the project renamed")]
    [InlineData("the project's file")]
    [InlineData("the startup object")]
    [InlineData("the startup form")]
    [InlineData("its references")]
    [InlineData("another choice")]
    [InlineData("the launch registered again")]
    [InlineData("another startup project")]
    public void F5_s_state_asks_the_chosen_launch_again_when_what_it_is_told_of_the_project_changes(string change)
    {
        var (project, module) = ContributedAndChosen();
        // The first form added is the startup object.
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        var second = new FormDefinition(project, FormComponentClass.Instance, "Form2");
        project.AddForm(form);
        project.AddForm(second);
        _runner.CanStartDefaultProject.Should().BeTrue();
        var requeried = 0;
        _runner.PropertyChanged += (_, e) => requeried += e.PropertyName == nameof(ProjectRunnerService.CanStartDefaultProject) ? 1 : 0;

        switch (change)
        {
            case "a module added": project.AddModule(new ModuleDefinition(project, "Accounts", ModuleKind.StandardModule)); break;
            case "a module deleted": project.DeleteModule(module); break;
            case "a module renamed": module.Name = "Main"; break;
            case "a module's file": module.AbsolutePath = Path.Join(Path.GetTempPath(), "Startup.bas"); break;
            case "a form added": project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form3")); break;
            case "a form deleted": project.DeleteForm(second); break;
            case "a form renamed":
                // As a rename that doesn't go through the designer says it: automation's set_control_property.
                second.Components[0].SetProperty(VBProperties.NameProperty, "Totals");
                second.NotifyRootPropertiesChanged();
                break;
            case "a form's file": second.AbsolutePath = Path.Join(Path.GetTempPath(), "Form2.frm"); break;
            case "the project renamed": project.Name = "Accounts"; break;
            case "the project's file": project.AbsolutePath = Path.Join(Path.GetTempPath(), ".rdproj"); break;
            case "the startup object": project.StartsAtSubMain = true; break;
            case "the startup form": project.StartupForm = second; break;
            case "its references": project.SetReferences([new VbReference("", "", 0, "scrrun.dll", "Scripting")]); break;
            case "another choice":
                _launches.Register(_launches.Find(Key)!.Launch with { Title = "Other" }, "com.rdcore.hexide", "RDCore for HexIDE");
                _choices.Set(project, "com.rdcore.hexide/Other");
                break;
            case "the launch registered again": Reregister(); break;
            case "another startup project":
                var other = new ProjectDefinition(VBProjectType.EXE, "Other") { ContributedBy = "RDCore projects" };
                _choices.Set(other, Key);
                _projects.StartupProject.Returns(other);
                break;
        }
        _runner.CanStartDefaultProject.Should().BeTrue();

        _claimsAsked.Should().Be(2);
        requeried.Should().BeGreaterThan(0, "F5's state is read again when its answer is let go of");
    }

    [AvaloniaFact]
    public void An_answer_given_while_the_project_changed_is_not_kept()
    {
        // The change comes while the launch is being asked, as one from elsewhere would come between the question and its
        // answer; the answer is to a project that is no longer there.
        ProjectDefinition? ledger = null;
        var changed = false;
        (ledger, _) = ContributedAndChosen(() =>
        {
            if (!changed)
            {
                changed = true;
                ledger!.Name = "Accounts";
            }
            return true;
        });

        _runner.CanStartDefaultProject.Should().BeTrue();
        _runner.CanStartDefaultProject.Should().BeTrue();
        _runner.CanStartDefaultProject.Should().BeTrue();

        _claimsAsked.Should().Be(2, "the first answer was let go of, and the second one kept");
    }

    // What F5's state kept is let go of once it is no use, project and launch alike: a launch's delegates are its
    // add-in's, in a load context let go of with it. One that still held them would still watch the project, and read
    // F5's state again when it changed.
    [AvaloniaTheory]
    [InlineData("the choice cleared")]
    [InlineData("the launch taken out")]
    [InlineData("the project unloaded")]
    public void F5_s_state_lets_go_of_the_project_and_the_launch_once_it_has_no_use_for_its_answer(string change)
    {
        var (project, _) = ContributedAndChosen();
        _runner.CanStartDefaultProject.Should().BeTrue();

        // Each on its own: F5's state read again finds no choice; the launch is taken out, and nothing reads F5's state;
        // the project is unloaded, and the startup project is none.
        switch (change)
        {
            case "the choice cleared":
                _choices.Set(project, null);
                _runner.CanStartDefaultProject.Should().BeFalse();
                break;
            case "the launch taken out": _registrations.Dispose(); break;
            case "the project unloaded":
                _projects.StartupProject.Returns((ProjectDefinition?)null);
                _projects.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);
                break;
        }
        var requeried = 0;
        _runner.PropertyChanged += (_, e) => requeried += e.PropertyName == nameof(ProjectRunnerService.CanStartDefaultProject) ? 1 : 0;

        project.Name = "Accounts";
        project.Modules[0].Name = "Main";

        requeried.Should().Be(0, "nothing of the project is watched any more");
        _claimsAsked.Should().Be(1);
    }

    // The launch taken out and contributed again: another registration, which may answer otherwise.
    private void Reregister()
    {
        var launch = _launches.Find(Key)!;
        _launches.Launches.Should().ContainSingle();
        _registrations.Dispose();
        _registrations = _launches.Register(launch.Launch, "com.rdcore.hexide", "RDCore for HexIDE");
    }

    [AvaloniaFact]
    public void Choosing_a_launch_or_its_coming_and_going_says_whether_F5_can_start()
    {
        var changed = new List<string?>();
        _runner.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        using (_launches.Register(new AddinDebugLaunch("RDCore") { Claims = _ => true, CreateConfigurationAsync = (_, _) => throw new NotSupportedException() },
            "com.rdcore.hexide", "RDCore for HexIDE"))
        {
            _choices.Set(_project, Key);
        }

        changed.Should().Equal(Enumerable.Repeat(nameof(ProjectRunnerService.CanStartDefaultProject), 3),
            "the launch came, was chosen and went");
    }
}
