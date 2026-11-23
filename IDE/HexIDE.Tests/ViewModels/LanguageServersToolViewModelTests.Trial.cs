using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Events;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Tests.LspClient;
using HexIDE.Tools.LanguageServers;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.ViewModels;

public partial class LanguageServersToolViewModelTests
{
    private sealed class TrialFixture : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("hexide-window-trial-").FullName;
        public string Marker => Path.Combine(_directory, "ran");
        public ILanguageServerLaunchConfirmation Confirmation { get; } = Substitute.For<ILanguageServerLaunchConfirmation>();
        public ILanguageConnectionRegistry Registry { get; } = Substitute.For<ILanguageConnectionRegistry>();
        public LanguageServerCommandStore Seen { get; }
        public LanguageServerEditorStore Store { get; }

        public TrialFixture(params LanguageServerEntry[] entries)
        {
            Seen = new LanguageServerCommandStore(Path.Combine(_directory, "seen.json"));
            Store = new LanguageServerEditorStore(Path.Combine(_directory, "servers.json"), entries,
                NullLogger<LanguageServerConfigLoader>.Instance, Seen);
            Registry.Connections.Returns([]);
            Registry.ConfigurationProblems.Returns([]);
            Confirmation.ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>()).Returns(true);
        }

        public LanguageServersToolViewModel Window(LanguageServersToolViewModelTests owner)
        {
            var localization = new LocalizationService();
            localization.Apply("en");
            var trial = new LanguageServerTrial(NullLoggerFactory.Instance, Seen, Confirmation, capture: owner._capture);
            var constructor = typeof(LanguageServersToolViewModel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .SingleOrDefault(constructor => constructor.GetParameters().Length == 9);
            constructor.Should().NotBeNull("the window must accept its gated trial");
            return (LanguageServersToolViewModel)constructor!.Invoke(
                [Registry, localization, owner._capture, owner._events, owner._pseudonyms, null, Store, null, trial]);
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }

    // Reflect only the new UI surface so these tests also compile on the reviewed pre-UI base,
    // where their first assertion fails rather than their build failing.
    private sealed class TrialRow(LanguageServerDefinitionViewModel row)
    {
        private T Read<T>(string name) => (T)row.GetType()
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(row)!;
        public IAsyncRelayCommand TestCommand => Read<IAsyncRelayCommand>("TestCommand");
        public ICommand ShowMessagesCommand => Read<ICommand>("ShowMessagesCommand");
        public bool HasResult => Read<bool>("HasResult");
        public bool HasMessages => Read<bool>("HasMessages");
        public string TestLabel => Read<string>("TestLabel");
        public string ResultText => Read<string>("ResultText");
        public LanguageServerTrialResult? Result => Read<LanguageServerTrialResult?>("Result");
        public IReadOnlyList<LanguageServerStepViewModel> Steps => Read<IReadOnlyList<LanguageServerStepViewModel>>("Steps");
    }

    private static TrialRow Row(LanguageServersToolViewModel vm, string? id = null) =>
        new(id is null ? vm.Definitions.Single() : vm.Definitions.Single(row => row.Id == id));

    private static LanguageServerEntry MissingEntry(bool enabled = true) => new()
    {
        Id = "missing", DisplayName = "Missing server", Transport = "stdio",
        Command = "hexide-nonexistent-server-1209", LanguageId = "probe", Extensions = [".probe"], Enabled = enabled,
    };

    [AvaloniaFact]
    public async Task TrialMissingCommandReportsConnectingDetailAndStopWithoutRegistration()
    {
        using var fixture = new TrialFixture(MissingEntry());
        var vm = fixture.Window(this);
        var row = Row(vm);
        await row.TestCommand.ExecuteAsync(null);
        row.HasResult.Should().BeTrue();
        row.Result!.Outcome.Should().Be(LanguageServerTrialOutcome.Failed);
        row.ResultText.Should().Contain("Connecting").And.Contain("Stop completed.");
        // No start error here, so the stage it stopped at completes the first line instead of a bare "initialize:".
        row.ResultText.Split(Environment.NewLine)[0].Should().StartWith("The server did not initialize: Connecting: ");
        row.Steps.Should().Contain(step => step.IsBreak && step.HasDetail);
        fixture.Registry.Connections.Should().BeEmpty();
        row.ShowMessagesCommand.Execute(null);
        _events.Received().Publish(Arg.Is<OpenProtocolInspectorEvent>(e => e.ConnectionId == "test:missing"));
    }

    [AvaloniaFact]
    public async Task TrialDisabledEntryStillRunsAndExplainsItIsDisabled()
    {
        using var fixture = new TrialFixture(MissingEntry(false));
        var row = Row(fixture.Window(this));
        await row.TestCommand.ExecuteAsync(null);
        row.Result!.Disabled.Should().BeTrue();
        row.Result.Outcome.Should().Be(LanguageServerTrialOutcome.Failed);
        row.ResultText.Should().Contain("switched off");
        await fixture.Confirmation.Received(1).ConfirmLaunchAsync("missing", "Missing server",
            Arg.Is<string>(command => command.Contains("hexide-nonexistent-server-1209")), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task TrialDeclineRunsNothingAndKeepsCommandUnseen()
    {
        using var fixture = new TrialFixture(MissingEntry());
        var entry = new LanguageServerEntry
        {
            Id = "marker", Transport = "stdio", LanguageId = "probe", Extensions = [".probe"],
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/mkdir",
            Arguments = new ServerArguments(null, OperatingSystem.IsWindows()
                ? ["/c", "mkdir", fixture.Marker] : ["-p", fixture.Marker]),
        };
        // Use a user entry, just as a file changed after IDE startup would supply.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Marker)!, "servers.json"),
            JsonSerializer.Serialize(new { version = 1, servers = new[] { entry } }));
        fixture.Confirmation.ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(false);
        var row = Row(fixture.Window(this), "marker");
        await row.TestCommand.ExecuteAsync(null);
        row.Result!.Outcome.Should().Be(LanguageServerTrialOutcome.Declined);
        row.ResultText.Should().Be("Nothing was run.");
        Directory.Exists(fixture.Marker).Should().BeFalse();
        fixture.Seen.IsNewOrChanged("marker", LanguageServerConfigLoader.LaunchedCommandOf(entry)!).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task TrialConfirmsTheSelectedResolvedEntryOnceAndExcludesUnknownRows()
    {
        using var fixture = new TrialFixture(MissingEntry());
        var userEntry = new LanguageServerEntry
        {
            Id = "other", DisplayName = "Missing server", Command = "other-command", Transport = "stdio",
            LanguageId = "probe", Extensions = [".probe"],
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Marker)!, "servers.json"),
            JsonSerializer.Serialize(new { version = 1, servers = new[] { userEntry } }));
        var vm = fixture.Window(this);
        var loads = 0;
        IReadOnlyList<LanguageServerEntry>? loaded = null;
        fixture.Store.Reloaded += entries => { loads++; loaded = entries; };
        var row = Row(vm, "other");
        await row.TestCommand.ExecuteAsync(null);
        loads.Should().Be(1);
        await fixture.Confirmation.Received(1).ConfirmLaunchAsync("other", "Missing server",
            LanguageServerConfigLoader.LaunchedCommandOf(loaded!.Single(entry => entry.Id == "other"))!,
            Arg.Any<CancellationToken>());
        fixture.Seen.IsNewOrChanged("other", row.Result!.Command!).Should().BeFalse();
        row.Result.Command.Should().Contain("other-command");
        fixture.Registry.Connections.Should().BeEmpty();
        typeof(LanguageServersToolViewModel).GetMethod("RefreshDefinitions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, null);
        Row(vm, "other").HasResult.Should().BeTrue("editing another definition must retain the result");
        // Removing a displayed entry from the resolved configuration must not test another entry instead.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Marker)!, "servers.json"),
            """{"version":1,"servers":[]}""");
        await row.TestCommand.ExecuteAsync(null);
        row.HasResult.Should().BeFalse();
        await fixture.Confirmation.Received(1).ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task TrialOnlyOneRunPerRowAndTestingLabelWhileConfirmationPending()
    {
        using var fixture = new TrialFixture(MissingEntry());
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Confirmation.ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>()).Returns(pending.Task);
        var vm = fixture.Window(this);
        var row = Row(vm);
        var run = row.TestCommand.ExecuteAsync(null);
        row.TestLabel.Should().Be("Testing…");
        row.TestCommand.CanExecute(null).Should().BeFalse();
        vm.OnClose().Should().BeTrue();
        pending.SetResult(false);
        await run;
        row.HasResult.Should().BeFalse("a trial completing after the window closes must not restore its result");
        row.TestLabel.Should().Be("Test");
        row.TestCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task TrialSilentTcpServerReportsInitializeTimeout()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var fixture = new TrialFixture(new LanguageServerEntry
        {
            Id = "silent", Transport = "tcp", Endpoint = $"tcp://127.0.0.1:{endpoint.Port}", LanguageId = "probe", Extensions = [".probe"],
        });
        var row = Row(fixture.Window(this));
        var run = row.TestCommand.ExecuteAsync(null);
        using var connection = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var bytes = new byte[65536];
        var received = "";
        while (!received.Contains("initialize", StringComparison.Ordinal))
        {
            var read = await connection.GetStream().ReadAsync(bytes, TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            read.Should().BeGreaterThan(0);
            received += System.Text.Encoding.UTF8.GetString(bytes, 0, read);
        }
        await run.WaitAsync(TimeSpan.FromSeconds(90), TestContext.Current.CancellationToken);
        row.Result!.Outcome.Should().Be(LanguageServerTrialOutcome.Failed);
        row.ResultText.Should().Contain("connected but did not answer initialize").And.Contain("Stop completed.");
        fixture.Registry.Connections.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(LanguageServerTrialOutcome.Declined, "Nothing was run.")]
    [InlineData(LanguageServerTrialOutcome.NotLaunchable, "cannot be started")]
    [InlineData(LanguageServerTrialOutcome.WaitingForWorkspace, "needs an open project")]
    [InlineData(LanguageServerTrialOutcome.Initialized, "server initialized")]
    [InlineData(LanguageServerTrialOutcome.Failed, "did not initialize")]
    public async Task TrialResultRendersEveryOutcome(LanguageServerTrialOutcome outcome, string expected)
    {
        var result = new LanguageServerTrialResult(outcome, "probe", null, false, false, null, null,
            ["zProvider", "aProvider"], ["hover"], null, null);
        var row = ResultRow(result);
        await row.TestCommand.ExecuteAsync(null);
        row.ResultText.Should().Contain(expected);
        // Nothing connected after a decline or for an entry that can't start, so there are no messages to show.
        row.HasMessages.Should().Be(outcome is not (LanguageServerTrialOutcome.Declined or LanguageServerTrialOutcome.NotLaunchable));
        if (outcome == LanguageServerTrialOutcome.Initialized)
        {
            row.ResultText.Should().Contain("reported no name or version")
                .And.Contain("aProvider, zProvider").And.Contain("Client capabilities declined: hover");
        }
    }

    [AvaloniaFact]
    public async Task TrialResultShowsReportedIdentityArtifactWarningAndStopError()
    {
        var result = new LanguageServerTrialResult(LanguageServerTrialOutcome.Initialized, "probe", null,
            false, false, null, new ServerIdentity("Example", "1.2"), [], [], null, "stop detail");
        var row = ResultRow(result);
        await row.TestCommand.ExecuteAsync(null);
        row.ResultText.Should().Contain("Example 1.2").And.Contain("Stop failed: stop detail");
        row = ResultRow(result with { Outcome = LanguageServerTrialOutcome.Failed, ArtifactNotGenerated = true });
        await row.TestCommand.ExecuteAsync(null);
        row.ResultText.Should().Contain("only a real start generates");
    }

    private TrialRow ResultRow(LanguageServerTrialResult result)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var constructor = typeof(LanguageServerDefinitionViewModel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(constructor => constructor.GetParameters().Length == 6);
        constructor.Should().NotBeNull("definition rows must expose isolated test results");
        return new TrialRow((LanguageServerDefinitionViewModel)constructor!.Invoke(
            ["probe", false, new HexIDE.Utils.DelegateCommand(() => { }), localization,
                (Func<Task<LanguageServerTrialResult?>>)(() => Task.FromResult<LanguageServerTrialResult?>(result)), _events]));
    }

    [ForeignServerFact("markdown")]
    public async Task TrialRumdlInitializesReportsCapabilitiesIdentityAndStops()
    {
        var server = ForeignServer.Markdown;
        using var fixture = new TrialFixture(new LanguageServerEntry
        {
            Id = "rumdl", Transport = "stdio", Command = server.Find()!,
            Arguments = new ServerArguments(server.LaunchArguments, null), Extensions = server.Extensions,
            LanguageId = server.LanguageId,
        });
        var row = Row(fixture.Window(this));
        await row.TestCommand.ExecuteAsync(null);
        row.Result!.Outcome.Should().Be(LanguageServerTrialOutcome.Initialized);
        row.ResultText.Should().Contain("rumdl").And.Contain("textDocumentSync").And.Contain("Stop completed.");
        row.Result.StopError.Should().BeNull();
        fixture.Registry.Connections.Should().BeEmpty();
    }
}
