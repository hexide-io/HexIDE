using Avalonia.Headless.XUnit;
using HexIDE.Addins;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// What a tool that needs a run engine says when none runs the startup project (#3514). A project an add-in read is the
/// case that exists today: the IDE's own engine answers no for it, and no fake is needed.
/// </summary>
public class NoRunEngineReplyTests : CompositionTestBase
{
    private const string NoEngine = "No run engine in this IDE runs 'Ledger'";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HexIdeTools ToolsOver(IProjectRunnerService runner, IProjectManager projects) =>
        new(new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(), null!, runner, null!,
            null!, null!, null!, Composition.DebugController, Composition.Root, null!, null!, null!, null!, Composition.Capture,
            null!, Composition.LocalizationService, null!, new Vb6ProjectFileFormat()));

    private HexIdeTools Tools() => ToolsOver(Composition.ProjectRunnerService, Composition.ProjectManager);

    private ProjectDefinition AnAddInsProjectAtStartup()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Ledger") { Offers = ProjectOffers.Nothing, ContributedBy = "rdcore", ContributorTitle = "RDCore" };
        Composition.ProjectManager.AddProject(project);
        Composition.ProjectManager.StartupProject = project;
        return project;
    }

    private static AddinDebugLaunch Launch(string title) => new(title)
    {
        Claims = _ => true,
        CreateConfigurationAsync = (_, _) => throw new InvalidOperationException("not started in this test"),
    };

    public static TheoryData<string> Starts => ["run_project", "step_into", "step_over", "step_out", "run_to_cursor"];

    private async Task<MutateResult> Start(HexIdeTools tools, string tool) => tool switch
    {
        "run_project" => await tools.RunProjectAsync(Ct),
        "step_into" => await tools.StepIntoAsync(Ct),
        "step_over" => await tools.StepOverAsync(Ct),
        "step_out" => await tools.StepOutAsync(Ct),
        "run_to_cursor" => await tools.RunToCursorAsync("Module1", 1, ct: Ct),
        _ => throw new ArgumentException(tool),
    };

    [AvaloniaTheory]
    [MemberData(nameof(Starts))]
    public async Task A_start_says_no_engine_runs_the_project_and_starts_nothing(string tool)
    {
        AnAddInsProjectAtStartup();

        var reply = await Start(Tools(), tool);

        reply.Success.Should().BeFalse();
        reply.Error.Should().StartWith(NoEngine).And.Contain("RDCore");
        reply.Error.Should().NotContain("already running").And.NotContain("load a project first");
        Composition.ProjectRunnerService.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task With_no_launch_the_reply_says_so_and_names_the_external_session()
    {
        AnAddInsProjectAtStartup();

        var reply = await Tools().RunProjectAsync(Ct);

        reply.Error.Should().Contain("No add-in offers a launch")
            .And.Contain("answer_next_file_dialog")
            .And.Contain("'Run/Start External Debug Session'");
    }

    [AvaloniaFact]
    public async Task With_launches_the_reply_names_each_as_the_menu_shows_it()
    {
        AnAddInsProjectAtStartup();
        using var debug = Composition.HexIdeHost.RegisterDebugLaunch(Launch("Debug"));
        using var profile = Composition.HexIdeHost.RegisterDebugLaunch(Launch("Profile"));
        var menu = Composition.Root.RunWith!;
        menu.Refresh();

        var reply = await Tools().RunProjectAsync(Ct);

        menu.Starts.Should().HaveCount(2);
        foreach (var item in menu.Starts)
            reply.Error.Should().Contain($"'Run/Start Once With/{item.Header}'");
        reply.Error.Should().NotContain("No add-in offers a launch");
    }

    [AvaloniaFact]
    public async Task The_menu_paths_it_names_are_in_the_IDE_s_own_language()
    {
        AnAddInsProjectAtStartup();
        using var _ = Composition.HexIdeHost.RegisterDebugLaunch(Launch("Debug"));
        var strings = Substitute.For<HexIDE.Localization.ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        strings.GetString("Str.Menu.Run").Returns("_Ausführen");
        strings.GetString("Str.Menu.Run.StartWith").Returns("Einmal _starten mit");
        strings.GetString("Str.Menu.Run.RunWith.Launch").Returns("{0} (von {1})");
        var tools = new HexIdeTools(new IdeContext(Composition.ProjectManager, Substitute.For<IDocumentDockService>(),
            Substitute.For<ILspClient>(), null!, Composition.ProjectRunnerService, null!, null!, null!, null!,
            Composition.DebugController, Composition.Root, null!, null!, null!, null!, Composition.Capture, null!, strings, null!, new Vb6ProjectFileFormat()));

        var reply = await tools.RunProjectAsync(Ct);

        reply.Error.Should().Contain("'Ausführen/Einmal starten mit/Debug (von ");
    }

    [AvaloniaFact]
    public async Task What_reads_a_paused_program_says_no_engine_runs_it_instead_of_sending_the_caller_to_run_project()
    {
        AnAddInsProjectAtStartup();
        var tools = Tools();

        string?[] refusals =
        [
            (await tools.GetLocalsAsync(ct: Ct)).Error,
            (await tools.GetCallStackAsync(ct: Ct)).Error,
            (await tools.EvaluateAsync("1", Ct)).Error,
        ];

        refusals.Should().AllSatisfy(error =>
        {
            error.Should().StartWith("No project is running, so ").And.Contain(NoEngine);
            error.Should().NotContain("then run_project");
        });
    }

    [AvaloniaFact]
    public async Task What_drives_a_run_says_no_engine_runs_it()
    {
        AnAddInsProjectAtStartup();
        var tools = Tools();

        string?[] refusals =
        [
            (await tools.BreakProgramAsync(Ct)).Error,
            (await tools.ContinueProgramAsync(Ct)).Error,
            (await tools.StopProjectAsync(Ct)).Error,
        ];

        refusals.Should().AllSatisfy(error => error.Should().Contain(NoEngine));
    }

    [AvaloniaFact]
    public async Task Set_next_statement_says_no_engine_runs_it()
    {
        var project = AnAddInsProjectAtStartup();
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub A()\r\nEnd Sub\r\n");
        project.AddModule(module);

        var reply = await Tools().SetNextStatementAsync("Module1", 2, ct: Ct);

        reply.Error.Should().Contain(NoEngine);
    }

    [AvaloniaFact]
    public async Task What_reports_on_a_run_says_no_engine_runs_it()
    {
        AnAddInsProjectAtStartup();
        var tools = Tools();

        (await tools.GetDebugStateAsync(Ct)).Note.Should().Contain(NoEngine);
        (await tools.GetWatchesAsync(Ct)).Note.Should().Contain(NoEngine);
        (await tools.AddWatchAsync("x", ct: Ct)).Note.Should().Contain(NoEngine);
        (await tools.GetLastRuntimeErrorAsync(Ct)).Note.Should().Contain(NoEngine);
    }

    [AvaloniaFact]
    public async Task A_project_the_engine_runs_keeps_its_replies()
    {
        Composition.ProjectManager.AddProject(TestHelpers.CreateProject("Invoices"));
        var tools = Tools();

        (await tools.GetLocalsAsync(ct: Ct)).Error.Should().Contain("then run_project");
        (await tools.BreakProgramAsync(Ct)).Error.Should().Be("No project is running");
        (await tools.ContinueProgramAsync(Ct)).Error.Should().Be("Project is not paused");
        (await tools.StopProjectAsync(Ct)).Error.Should().Be("No project is currently running");
        (await tools.GetDebugStateAsync(Ct)).Note.Should().BeNull();
        (await tools.GetWatchesAsync(Ct)).Note.Should().BeNull();
        (await tools.GetLastRuntimeErrorAsync(Ct)).Note.Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData("run_project", "No project loaded")]
    [InlineData("step_into", "No project to step — load a project first")]
    [InlineData("run_to_cursor", "No project to run — load a project first")]
    public async Task With_no_project_it_still_says_to_load_one(string tool, string said)
    {
        var reply = await Start(Tools(), tool);

        reply.Error.Should().Be(said);
    }

    [AvaloniaFact]
    public async Task A_refused_start_only_says_already_running_when_a_project_is()
    {
        var runner = Substitute.For<IProjectRunnerService>();
        var projects = Substitute.For<IProjectManager>();
        projects.StartupProject.Returns(TestHelpers.CreateProject("Invoices"));
        var tools = ToolsOver(runner, projects);

        var focused = await tools.RunProjectAsync(Ct);

        focused.Error.Should().NotContain("already running").And.Contain("can't be started now").And.Contain("archive");

        runner.IsRunning.Returns(true);
        (await tools.RunProjectAsync(Ct)).Error.Should().Be("Project is already running");
    }

    [AvaloniaFact]
    public void The_runner_names_a_startup_project_only_while_nothing_can_run_it()
    {
        var runner = Composition.ProjectRunnerService;
        runner.StartupWithNoEngine.Should().BeNull("there is no project at all");

        Composition.ProjectManager.AddProject(TestHelpers.CreateProject("Invoices"));
        runner.StartupWithNoEngine.Should().BeNull("the engine runs a project HexIDE read");

        var ledger = AnAddInsProjectAtStartup();
        runner.StartupWithNoEngine!.Project.Should().BeSameAs(ledger);
        runner.StartupWithNoEngine!.Launches.Should().BeEmpty();
    }
}
