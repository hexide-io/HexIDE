using Avalonia.Headless.XUnit;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.DebugAdapter;

/// <summary>
/// What F5 runs: the interpreter, unless the project's user chose an add-in's launch for it, and the interpreter again,
/// said so, when that launch is gone.
/// </summary>
public class RunWithTests : IAsyncDisposable
{
    private const string Key = "RDCore for HexIDE/RDCore";

    private readonly ConversationLog _log = new();
    private readonly ProjectDefinition _project = TestHelpers.CreateProject("Invoices");
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
        var projects = Substitute.For<IProjectManager>();
        projects.StartupProject.Returns(_project);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString("Str.Debug.RunWith.Unavailable").Returns("{0} isn't available");
        _runner = new ProjectRunnerService(Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), projects, strings,
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

    private void Contribute(Func<AddinProjectInfo, bool>? claims = null) =>
        _launches.Register(new AddinDebugLaunch("RDCore")
        {
            Claims = claims ?? (_ => true),
            CreateConfigurationAsync = (project, _) =>
            {
                _asked.TrySetResult(project);
                throw new InvalidOperationException("this test stops at the configuration");
            },
        }, "RDCore for HexIDE");

    [AvaloniaFact]
    public void F5_runs_the_interpreter_though_an_add_in_offers_a_launch_until_the_user_chooses_it()
    {
        Contribute();

        _runner.RunStartupProject();

        _interpreterStarts.Should().ContainSingle();
        _asked.Task.IsCompleted.Should().BeFalse("installing an add-in changes nothing about F5");
    }

    [AvaloniaFact]
    public async Task F5_runs_the_launch_its_user_chose_for_the_project()
    {
        Contribute();
        _choices.Set(_project, Key);

        _runner.RunStartupProject();

        (await _asked.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)).ProjectName.Should().Be("Invoices");
        _interpreterStarts.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Stepping_in_from_idle_stays_the_interpreter_s_whatever_F5_runs()
    {
        Contribute();
        _choices.Set(_project, Key);

        _runner.RunStartupProject(stepInto: true);

        _interpreterStarts.Should().ContainSingle();
        _asked.Task.IsCompleted.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("gone")]
    [InlineData("no longer claims it")]
    public void A_chosen_launch_that_is_not_there_falls_back_to_the_interpreter_and_says_so(string why)
    {
        if (why != "gone") Contribute(claims: _ => false);
        _choices.Set(_project, Key);

        _runner.RunStartupProject();

        _interpreterStarts.Should().ContainSingle();
        _status.Received(1).SetTemporaryMessage($"{Key} isn't available", Arg.Any<TimeSpan>());
        _choices.Get(_project).Should().Be(Key, "the choice stays, for when the add-in is back");
    }
}
