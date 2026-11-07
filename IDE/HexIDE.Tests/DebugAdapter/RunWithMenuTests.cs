using Avalonia.Headless.XUnit;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.DebugAdapter;

/// <summary>
/// The Run menu's F5 Runs and Start Once With: hidden until an add-in offers a launch for the startup project, or, for F5
/// Runs, until the project has a choice; then the interpreter and each launch, with what F5 runs checked. Built only as
/// the Run menu opens.
/// </summary>
public class RunWithMenuTests : CompositionTestBase
{
    private static AddinDebugLaunch Launch(string title, Func<AddinProjectInfo, bool>? claims = null) => new(title)
    {
        Claims = claims ?? (_ => true),
        CreateConfigurationAsync = (_, _) => throw new InvalidOperationException("not started in this test"),
    };

    [AvaloniaFact]
    public void An_add_in_s_launch_reaches_the_Run_menu_through_the_host()
    {
        var project = TestHelpers.CreateProject("Invoices");
        Composition.ProjectManager.StartupProject = project;
        var menu = Composition.Root.RunWith;
        menu.Should().NotBeNull();
        menu!.Refresh();
        menu.HasLaunches.Should().BeFalse();

        using var launch = Composition.HexIdeHost.RegisterDebugLaunch(Launch("RDCore"));
        menu.Refresh();

        menu.HasLaunches.Should().BeTrue();
        menu.Starts.Should().ContainSingle().Which.Header.Should().EndWith(
            $"(from {typeof(RunWithMenuTests).Assembly.GetName().Name})", "this test's assembly is in no add-in's load context");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public readonly ConversationLog Log = new();
        public readonly DebugLaunchService Launches;
        public readonly RunChoiceStore Choices = new();
        public readonly IProjectManager Projects = Substitute.For<IProjectManager>();
        public readonly IProjectRunnerService Runner = Substitute.For<IProjectRunnerService>();
        public readonly ProjectDefinition Project = TestHelpers.CreateProject("Invoices");
        public readonly RunWithMenuViewModel Menu;

        public Fixture()
        {
            Launches = new DebugLaunchService(new ExternalDebugService(Log));
            Projects.StartupProject.Returns(Project);
            var strings = Substitute.For<ILocalizationService>();
            strings.GetString("Str.Menu.Run.RunWith.Interpreter").Returns("In the IDE");
            strings.GetString("Str.Menu.Run.RunWith.Launch").Returns("{0} (from {1})");
            strings.GetString("Str.Menu.Run.RunWith.Unavailable").Returns("{0} (not available)");
            Menu = new RunWithMenuViewModel(Launches, Choices, Projects, Runner, strings);
        }

        public ValueTask DisposeAsync() => Log.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Run_With_lists_the_interpreter_then_the_launches_that_run_the_project_with_F5_s_checked()
    {
        await using var fixture = new Fixture();
        fixture.Launches.Register(Launch("RDCore"), "com.rdcore.hexide", "RDCore for HexIDE");
        fixture.Launches.Register(Launch("Elsewhere", claims: _ => false), "com.example.another", "Another add-in");
        fixture.Menu.Refresh();

        fixture.Menu.Choices.Select(item => (item.Header, item.IsChecked)).Should().Equal(
            ("In the IDE", true), ("RDCore (from RDCore for HexIDE)", false));
        fixture.Menu.Starts.Select(item => item.Header).Should().Equal("RDCore (from RDCore for HexIDE)");

        fixture.Menu.Choices[1].Command.Execute(null);
        fixture.Choices.Get(fixture.Project).Should().Be("com.rdcore.hexide/RDCore");
        fixture.Menu.Refresh();
        fixture.Menu.Choices.Select(item => item.IsChecked).Should().Equal(false, true);
        fixture.Menu.Choices[0].Command.Execute(null);
        fixture.Choices.Get(fixture.Project).Should().BeNull("the IDE is no choice to remember");
    }

    [AvaloniaFact]
    public async Task Start_With_runs_the_launch_once_without_changing_what_F5_runs_and_not_while_something_runs()
    {
        await using var fixture = new Fixture();
        fixture.Launches.Register(Launch("RDCore"), "com.rdcore.hexide", "RDCore for HexIDE");
        fixture.Menu.Refresh();

        fixture.Menu.Starts.Single().Command.Execute(null);

        fixture.Runner.Received(1).StartContributedLaunch(
            Arg.Is<ContributedDebugLaunch>(launch => launch.Key == "com.rdcore.hexide/RDCore"), fixture.Project);
        fixture.Choices.Get(fixture.Project).Should().BeNull();
        fixture.Runner.IsRunning.Returns(true);
        fixture.Menu.Starts.Single().Command.CanExecute(null).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_chosen_launch_that_is_gone_keeps_Run_With_shown_so_the_choice_can_be_set_back()
    {
        await using var fixture = new Fixture();
        fixture.Choices.Set(fixture.Project, "com.rdcore.hexide/RDCore");

        fixture.Menu.Refresh();

        fixture.Menu.HasLaunches.Should().BeFalse("there is nothing to start");
        fixture.Menu.HasChoices.Should().BeTrue("the fallback message sends the user here");
        fixture.Menu.Choices.Select(item => (item.Header, item.IsChecked, item.Command.CanExecute(null))).Should().Equal(
            ("In the IDE", false, true), ("RDCore (not available)", true, false));
        fixture.Menu.Choices[0].Command.Execute(null);
        fixture.Choices.Get(fixture.Project).Should().BeNull();
    }

    // Through the whole composition, as the guarantee is in the wiring: only the Run menu's opening calls Refresh. The
    // view's SubmenuOpened handler itself is checked by hand.
    [AvaloniaFact]
    public void A_launch_is_asked_whether_it_runs_the_project_only_as_the_Run_menu_opens()
    {
        var asked = 0;
        var menu = Composition.Root.RunWith!;
        using var launch = Composition.HexIdeHost.RegisterDebugLaunch(Launch("RDCore", claims: _ => ++asked > 0));
        var project = TestHelpers.CreateProject("Invoices");
        var other = TestHelpers.CreateProject("Ledger");

        // The first project loaded becomes the startup project only after it has loaded, so a second load and a change of
        // startup project are what find one to ask about.
        Composition.ProjectManager.AddProject(project);
        Composition.RunChoices.Set(project, $"{typeof(RunWithMenuTests).Assembly.GetName().Name}/RDCore");
        Composition.ProjectManager.AddProject(other);
        Composition.ProjectManager.StartupProject = other;
        Composition.ProjectManager.StartupProject = project;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        asked.Should().Be(0, "nothing is asked of an add-in when a project opens, becomes the startup project or has a choice");

        menu.Refresh();
        asked.Should().BeGreaterThan(0);
    }

    [AvaloniaFact]
    public async Task The_menu_empties_when_the_launch_is_taken_out()
    {
        await using var fixture = new Fixture();
        var handle = fixture.Launches.Register(Launch("RDCore"), "com.rdcore.hexide", "RDCore for HexIDE");
        fixture.Menu.Refresh();
        fixture.Menu.HasLaunches.Should().BeTrue();

        handle.Dispose();
        fixture.Menu.Refresh();

        fixture.Menu.HasLaunches.Should().BeFalse();
        fixture.Menu.Choices.Should().BeEmpty();
    }
}
