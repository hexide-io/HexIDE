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
/// The Run menu's Run With and Start With: hidden until an add-in offers a launch for the startup project, then the
/// interpreter and each launch, with what F5 runs checked.
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
        menu!.HasLaunches.Should().BeFalse();

        using var launch = Composition.HexIdeHost.RegisterDebugLaunch(Launch("RDCore"));

        menu.HasLaunches.Should().BeTrue();
        menu.Starts.Should().ContainSingle();
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
            strings.GetString("Str.Menu.Run.RunWith.Interpreter").Returns("The IDE");
            strings.GetString("Str.Menu.Run.RunWith.Launch").Returns("{0} (from {1})");
            Menu = new RunWithMenuViewModel(Launches, Choices, Projects, Runner, strings);
        }

        public ValueTask DisposeAsync() => Log.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Run_With_lists_the_interpreter_then_the_launches_that_run_the_project_with_F5_s_checked()
    {
        await using var fixture = new Fixture();
        fixture.Launches.Register(Launch("RDCore"), "RDCore for HexIDE");
        fixture.Launches.Register(Launch("Elsewhere", claims: _ => false), "Another add-in");

        fixture.Menu.Choices.Select(item => (item.Header, item.IsChecked)).Should().Equal(
            ("The IDE", true), ("RDCore (from RDCore for HexIDE)", false));
        fixture.Menu.Starts.Select(item => item.Header).Should().Equal("RDCore (from RDCore for HexIDE)");

        fixture.Menu.Choices[1].Command.Execute(null);

        fixture.Choices.Get(fixture.Project).Should().Be("RDCore for HexIDE/RDCore");
        fixture.Menu.Choices.Select(item => item.IsChecked).Should().Equal(false, true);
        fixture.Menu.Choices[0].Command.Execute(null);
        fixture.Choices.Get(fixture.Project).Should().BeNull("the IDE is no choice to remember");
    }

    [AvaloniaFact]
    public async Task Start_With_runs_the_launch_once_without_changing_what_F5_runs()
    {
        await using var fixture = new Fixture();
        fixture.Launches.Register(Launch("RDCore"), "RDCore for HexIDE");

        fixture.Menu.Starts.Single().Command.Execute(null);

        fixture.Runner.Received(1).StartContributedLaunch(
            Arg.Is<ContributedDebugLaunch>(launch => launch.Key == "RDCore for HexIDE/RDCore"), fixture.Project);
        fixture.Choices.Get(fixture.Project).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task The_menu_empties_when_the_launch_is_taken_out()
    {
        await using var fixture = new Fixture();
        var handle = fixture.Launches.Register(Launch("RDCore"), "RDCore for HexIDE");
        fixture.Menu.HasLaunches.Should().BeTrue();

        handle.Dispose();

        fixture.Menu.HasLaunches.Should().BeFalse();
        fixture.Menu.Choices.Should().BeEmpty();
    }
}
