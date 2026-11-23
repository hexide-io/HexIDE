using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.Logging;
using HexIDE.Conversations;
using HexIDE.Lsp;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// The Language Servers window's Test connection (#1209) launches a command, so its gates are pinned here: an unseen
/// command runs only on the person's say-so, the trial's client can't act on the IDE, it writes no workspace file,
/// and it never touches the live registry.
/// </summary>
public sealed class LanguageServerTrialTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("hexide-trial-").FullName;
    private readonly ILoggerFactory _logging = LoggerFactory.Create(_ => { });

    public void Dispose()
    {
        _logging.Dispose();
        try { Directory.Delete(_dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string SeenPath => Path.Combine(_dir, "seen.json");

    // A stdio entry whose command makes a directory and exits, so a test can tell whether it was launched at all.
    private static LanguageServerEntry MarkerEntry(string id, string marker, string? extra = null) => new()
    {
        Id = id,
        Transport = "stdio",
        Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/mkdir",
        Arguments = new ServerArguments(null, OperatingSystem.IsWindows()
            ? ["/c", "mkdir", marker, .. extra is null ? Array.Empty<string>() : [extra]]
            : ["-p", marker, .. extra is null ? Array.Empty<string>() : [extra]]),
        Extensions = [".probe"],
    };

    private static ILanguageServerLaunchConfirmation Answering(bool agree)
    {
        var confirmation = Substitute.For<ILanguageServerLaunchConfirmation>();
        confirmation.ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(agree);
        return confirmation;
    }

    [Fact]
    public async Task Declining_an_unseen_command_runs_nothing_and_leaves_it_unseen()
    {
        var marker = Path.Combine(_dir, "ran");
        var entry = MarkerEntry("probe", marker);
        var command = LanguageServerConfigLoader.LaunchedCommandOf(entry)!;
        var confirmation = Answering(false);
        var store = new LanguageServerCommandStore(SeenPath);

        var result = await new LanguageServerTrial(_logging, store, confirmation).RunAsync(entry, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LanguageServerTrialOutcome.Declined);
        result.Command.Should().Be(command);
        Directory.Exists(marker).Should().BeFalse("declining must launch nothing");
        await confirmation.Received(1).ConfirmLaunchAsync("probe", "probe", command, Arg.Any<CancellationToken>());
        store.IsNewOrChanged("probe", command).Should().BeTrue();
        new LanguageServerCommandStore(SeenPath).IsNewOrChanged("probe", command).Should().BeTrue();
    }

    [Fact]
    public async Task Agreeing_to_an_unseen_command_launches_it_and_records_it_as_seen()
    {
        var marker = Path.Combine(_dir, "ran");
        var entry = MarkerEntry("probe", marker);
        var command = LanguageServerConfigLoader.LaunchedCommandOf(entry)!;
        var store = new LanguageServerCommandStore(SeenPath);

        var result = await new LanguageServerTrial(_logging, store, Answering(true)).RunAsync(entry, TestContext.Current.CancellationToken);

        Directory.Exists(marker).Should().BeTrue("the person agreed, so the command ran");
        result.Outcome.Should().Be(LanguageServerTrialOutcome.Failed, "the marker command exits without speaking LSP");
        new LanguageServerCommandStore(SeenPath).IsNewOrChanged("probe", command).Should().BeFalse();
    }

    [Fact]
    public async Task A_seen_command_runs_without_asking()
    {
        var marker = Path.Combine(_dir, "ran");
        var entry = MarkerEntry("probe", marker);
        var store = new LanguageServerCommandStore(SeenPath);
        store.Record("probe", LanguageServerConfigLoader.LaunchedCommandOf(entry)!);
        var confirmation = Answering(false);

        await new LanguageServerTrial(_logging, store, confirmation).RunAsync(entry, TestContext.Current.CancellationToken);

        Directory.Exists(marker).Should().BeTrue();
        await confirmation.DidNotReceiveWithAnyArgs().ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Changed_arguments_are_a_new_command()
    {
        var marker = Path.Combine(_dir, "ran");
        var store = new LanguageServerCommandStore(SeenPath);
        store.Record("probe", LanguageServerConfigLoader.LaunchedCommandOf(MarkerEntry("probe", marker))!);
        var confirmation = Answering(false);

        var result = await new LanguageServerTrial(_logging, store, confirmation)
            .RunAsync(MarkerEntry("probe", marker, extra: Path.Combine(_dir, "also")), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LanguageServerTrialOutcome.Declined);
        Directory.Exists(marker).Should().BeFalse();
    }

    [Fact]
    public async Task The_bundled_id_earns_no_exemption()
    {
        var marker = Path.Combine(_dir, "ran");
        var entry = MarkerEntry(LanguageServerDefaults.BundledVb6Id, marker);
        var confirmation = Answering(false);

        var result = await new LanguageServerTrial(_logging, new LanguageServerCommandStore(SeenPath), confirmation)
            .RunAsync(entry, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LanguageServerTrialOutcome.Declined);
        Directory.Exists(marker).Should().BeFalse();
    }

    [Fact]
    public async Task A_prompt_that_is_cancelled_or_fails_to_show_runs_nothing()
    {
        var marker = Path.Combine(_dir, "ran");
        var confirmation = Substitute.For<ILanguageServerLaunchConfirmation>();
        confirmation.ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs<Task<bool>>(_ => throw new OperationCanceledException());

        var result = await new LanguageServerTrial(_logging, new LanguageServerCommandStore(SeenPath), confirmation)
            .RunAsync(MarkerEntry("probe", marker), TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(LanguageServerTrialOutcome.Declined);
        Directory.Exists(marker).Should().BeFalse();
    }

    [Fact]
    public void A_trial_cannot_be_made_without_a_confirmation_or_a_store()
    {
        var act1 = () => new LanguageServerTrial(_logging, new LanguageServerCommandStore(SeenPath), null!);
        var act2 = () => new LanguageServerTrial(_logging, null!, Answering(true));
        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task A_disabled_entry_is_gated_the_same_and_says_it_is_disabled()
    {
        var marker = Path.Combine(_dir, "ran");
        var entry = MarkerEntry("probe", marker);
        entry.Enabled = false;
        var confirmation = Answering(true);

        var result = await new LanguageServerTrial(_logging, new LanguageServerCommandStore(SeenPath), confirmation)
            .RunAsync(entry, TestContext.Current.CancellationToken);

        await confirmation.ReceivedWithAnyArgs(1).ConfirmLaunchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        result.Disabled.Should().BeTrue();
        Directory.Exists(marker).Should().BeTrue();
    }

    [Fact]
    public async Task A_declared_workspace_file_is_not_written_and_a_failure_says_it_may_need_one()
    {
        var marker = Path.Combine(_dir, "ran");
        var entry = MarkerEntry("probe", marker);
        entry.WorkspaceArtifact = new WorkspaceArtifactEntry { Path = "probe.rdproj", Provider = "probe" };
        var store = new LanguageServerCommandStore(SeenPath);
        store.Record("probe", LanguageServerConfigLoader.LaunchedCommandOf(entry)!);

        var result = await new LanguageServerTrial(_logging, store, Answering(false)).RunAsync(entry, TestContext.Current.CancellationToken);

        result.ArtifactNotGenerated.Should().BeTrue();
        result.MayNeedWorkspaceArtifact.Should().BeTrue("it failed, and the file only a real start writes may be why");
        Directory.EnumerateFiles(_dir, "*.rdproj", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task The_trials_client_holds_no_editor_callbacks()
    {
        var entry = MarkerEntry("probe", Path.Combine(_dir, "ran"));
        var trial = new LanguageServerTrial(_logging, new LanguageServerCommandStore(SeenPath), Answering(false));

        await using var client = trial.Factory.Create([entry], LanguageServerTrial.CaptureIdPrefix).Single().CreateClient();

        client.Should().BeOfType<VBLspClient>();
        foreach (var field in new[] { "_applyEdit", "_showDocument", "_showMessageRequest" })
            typeof(VBLspClient).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(client).Should().BeNull($"{field} would let a server under test act on the IDE");
    }

    [Fact]
    public async Task A_trial_leaves_the_live_registry_as_it_was()
    {
        var live = Substitute.For<ILspClient>();
        var registration = new LanguageServerRegistration("probe", "Probe", [".probe"], "probe", () => live);
        await using var registry = new LspClientRegistry([registration], _logging.CreateLogger<LspClientRegistry>());
        var before = registry.Connections.Select(c => (c.Id, c.State)).ToArray();
        var store = new LanguageServerCommandStore(SeenPath);
        var entry = MarkerEntry("probe", Path.Combine(_dir, "ran"));
        store.Record("probe", LanguageServerConfigLoader.LaunchedCommandOf(entry)!);

        await new LanguageServerTrial(_logging, store, Answering(false)).RunAsync(entry, TestContext.Current.CancellationToken);

        registry.Connections.Select(c => (c.Id, c.State)).Should().Equal(before);
        live.ReceivedCalls().Should().BeEmpty("the trial makes its own client and never reaches the live one");
    }

    [ForeignServerFact("markdown")]
    public async Task A_real_server_initializes_reports_itself_and_records_apart_from_the_live_connection()
    {
        var token = TestContext.Current.CancellationToken;
        var server = ForeignServer.Markdown;
        var entry = new LanguageServerEntry
        {
            Id = "rumdl",
            Transport = "stdio",
            Command = server.Find()!,
            Arguments = new ServerArguments(server.LaunchArguments, null),
            LanguageId = server.LanguageId,
            Extensions = [".md"],
        };
        var store = new LanguageServerCommandStore(SeenPath);
        store.Record("rumdl", LanguageServerConfigLoader.LaunchedCommandOf(entry)!);
        await using var capture = new ConversationLog();

        var result = await new LanguageServerTrial(_logging, store, Answering(false), capture: capture).RunAsync(entry, token);
        await capture.DrainAsync();

        result.Outcome.Should().Be(LanguageServerTrialOutcome.Initialized, result.StartError);
        result.Identity?.Name.Should().NotBeNullOrWhiteSpace();
        result.AdvertisedCapabilities.Should().Contain("textDocumentSync");
        result.StopError.Should().BeNull();
        capture.Snapshot(LanguageServerTrial.CaptureIdPrefix + "rumdl").Should().NotBeEmpty();
        capture.Snapshot("rumdl").Should().BeEmpty();
    }
}
