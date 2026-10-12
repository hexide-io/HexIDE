using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The foreign-server suite's one restart (#390) is given only on the full evidence of a silent server. Each test
/// below takes one piece of that evidence away and checks the gate refuses, so a retry cannot quietly start hiding a
/// fault of ours.
/// </summary>
public class SilentServerRestartTests
{
    private const string Id = "foreign.markdown";
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan GaveUp = TimeSpan.FromSeconds(30);

    private static LanguageServerConnection Connection(
        LanguageConnectionState state = LanguageConnectionState.Failed,
        bool handshakeSent = true,
        LanguageConnectionStepOutcome outcome = LanguageConnectionStepOutcome.StoppedHere,
        string detail = VBLspClient.InitializeTimeoutDetail + " 0:00:30")
    {
        List<LanguageConnectionStep> steps =
        [
            new(LanguageConnectionStage.Connecting, LanguageConnectionStepOutcome.Reached, TimeSpan.Zero, null),
            new(LanguageConnectionStage.Connected, LanguageConnectionStepOutcome.Reached, TimeSpan.FromMilliseconds(40), null),
        ];
        if (handshakeSent)
            steps.Add(new(LanguageConnectionStage.HandshakeSent, outcome, GaveUp, detail));
        else
            steps[^1] = steps[^1] with { Outcome = LanguageConnectionStepOutcome.StoppedHere };

        return new LanguageServerConnection(
            Id, "rumdl", LanguageConnectionKind.LanguageServer, state, [".md"], "markdown", null,
            Attempt: new LanguageConnectionAttempt(
                handshakeSent ? LanguageConnectionStage.HandshakeSent : LanguageConnectionStage.Connected, steps, Start));
    }

    private static ConversationEnvelope Note(double ms, string detail) =>
        new(0, Id, Start.AddMilliseconds(ms), ConversationDirection.Local, ConversationEntryKind.Lifecycle, null, null, 0,
            Detail: detail);

    private static ConversationEnvelope Initialize(double ms) =>
        new(0, Id, Start.AddMilliseconds(ms), ConversationDirection.Sent, ConversationEntryKind.Request, "initialize", "1", 397);

    /// <summary>The record of #390's stall, as the client writes it.</summary>
    private static List<ConversationEnvelope> SilentWire() =>
    [
        Note(0, "connecting"),
        Note(40, "connected"),
        Initialize(41),
        Note(41, ChannelTapStream.BeganWaitingNote),
        Note(41, $"began writing 397 {ChannelTapStream.BeganWritingNote}"),
        Note(42, $"the first 397 {ChannelTapStream.ReachedChannelNote}"),
    ];

    // ── the gate ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_full_evidence_of_a_silent_server_qualifies()
    {
        SilentServerRestart.Evidence(Connection(), SilentWire(), out _).Should().Contain("nothing came back");
    }

    [Fact]
    public void An_exit_after_the_handshake_gave_up_still_qualifies()
    {
        // The stall's usual ending: the abandoned server wakes, writes into the closed pipe and dies of SIGPIPE.
        var wire = SilentWire();
        wire.Add(Note(GaveUp.TotalMilliseconds + 500, $"{StdioProcessLspTransport.ProcessExitedNote} with code 141"));

        SilentServerRestart.Evidence(Connection(), wire, out _).Should().NotBeNull();
    }

    [Fact]
    public void Bytes_from_the_server_refuse_it()
    {
        var wire = SilentWire();
        wire.Add(Note(60, $"the first 836 {ChannelTapStream.ArrivedNote}"));

        SilentServerRestart.Evidence(Connection(), wire, out var refusal).Should().BeNull();
        refusal.Should().Be("bytes arrived from the server");
    }

    [Fact]
    public void An_exit_before_the_handshake_gave_up_refuses_it()
    {
        var wire = SilentWire();
        wire.Add(Note(5000, $"{StdioProcessLspTransport.ProcessExitedNote} with code 1"));

        SilentServerRestart.Evidence(Connection(), wire, out var refusal).Should().BeNull();
        refusal.Should().Be("the process exited before the handshake gave up");
    }

    [Fact]
    public void An_initialize_never_sent_refuses_it()
    {
        var wire = SilentWire().Where(e => e.Method != "initialize").ToList();

        SilentServerRestart.Evidence(Connection(handshakeSent: false), wire, out var refusal).Should().BeNull();
        refusal.Should().Contain("initialize was never sent");
    }

    [Fact]
    public void A_record_without_the_initialize_request_refuses_it()
    {
        var wire = SilentWire().Where(e => e.Method != "initialize").ToList();

        SilentServerRestart.Evidence(Connection(), wire, out var refusal).Should().BeNull();
        refusal.Should().Be("the record has no initialize request");
    }

    [Fact]
    public void Bytes_that_never_reached_the_channel_refuse_it()
    {
        var wire = SilentWire().Where(e => e.Detail?.EndsWith(ChannelTapStream.ReachedChannelNote) != true).ToList();

        SilentServerRestart.Evidence(Connection(), wire, out var refusal).Should().BeNull();
        refusal.Should().Contain("reach the server's channel");
    }

    [Fact]
    public void A_read_that_was_never_waiting_refuses_it()
    {
        var wire = SilentWire().Where(e => e.Detail != ChannelTapStream.BeganWaitingNote).ToList();

        SilentServerRestart.Evidence(Connection(), wire, out var refusal).Should().BeNull();
        refusal.Should().Contain("our read waiting");
    }

    [Fact]
    public void A_handshake_that_failed_rather_than_timed_out_refuses_it()
    {
        // What a server that exits leaves: the request fails at once, and its exit may not be recorded yet.
        SilentServerRestart.Evidence(Connection(detail: "The JSON-RPC connection with the remote party was lost"), SilentWire(), out var refusal)
            .Should().BeNull();
        refusal.Should().Contain("failed rather than timed out");
    }

    [Fact]
    public void A_cancelled_handshake_refuses_it()
    {
        SilentServerRestart.Evidence(Connection(outcome: LanguageConnectionStepOutcome.Cancelled), SilentWire(), out var refusal)
            .Should().BeNull();
        refusal.Should().Contain("Cancelled");
    }

    [Fact]
    public void A_running_connection_refuses_it()
    {
        SilentServerRestart.Evidence(Connection(LanguageConnectionState.Running), SilentWire(), out var refusal)
            .Should().BeNull();
        refusal.Should().Contain("Running");
    }

    // ── against a real silent process ─────────────────────────────────────────────────────────

    /// <summary>
    /// The gate reads notes by name, so a record shaped differently from the synthetic ones above would make it refuse
    /// every time, and the restart would quietly never happen. Here a real process that never reads or answers is
    /// driven through the real stdio transport, and the gate must recognise what it leaves.
    /// </summary>
    [Fact]
    public async Task The_gate_recognises_the_record_a_real_silent_process_leaves()
    {
        var mute = OperatingSystem.IsWindows()
            ? new LspServerInfo("cmd.exe", "/c ping -n 60 127.0.0.1 > nul", Path.GetTempPath())
            : new LspServerInfo("sleep", "60", Path.GetTempPath());
        var (connection, wire) = await DriveAsync(mute, TimeSpan.FromSeconds(1));

        SilentServerRestart.Evidence(connection, wire, out var refusal).Should().NotBeNull(
            "a process that never answers is the case the restart exists for; the gate refused because {0}", refusal);
    }

    [Fact]
    public async Task The_gate_refuses_the_record_a_real_process_that_exits_leaves()
    {
        var quitter = OperatingSystem.IsWindows()
            ? new LspServerInfo("cmd.exe", "/c exit 3", Path.GetTempPath())
            : new LspServerInfo("sh", "-c \"exit 3\"", Path.GetTempPath());
        var (connection, wire) = await DriveAsync(quitter, TimeSpan.FromSeconds(5));

        // Refused because the handshake failed the moment the pipe closed, before any timeout. Not because its exit
        // was recorded: under load that notice arrived after the handshake gave up, and an earlier gate qualified it.
        SilentServerRestart.Evidence(connection, wire, out var refusal).Should().BeNull(
            "a process that exits is not silent; it is gone");
        refusal.Should().Contain("failed rather than timed out");
    }

    private static async Task<(LanguageServerConnection, IReadOnlyList<ConversationEnvelope>)> DriveAsync(
        LspServerInfo server, TimeSpan initializeTimeout)
    {
        var capture = new ConversationLog();
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var registration = new LanguageServerRegistration(
            Id: Id,
            DisplayName: "mute",
            Extensions: [".md"],
            LanguageId: "markdown",
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(server, loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                loggerFactory.CreateLogger<VBLspClient>(),
                "markdown",
                initializeTimeout: initializeTimeout,
                capture: capture, connectionId: Id));
        var registry = new LspClientRegistry([registration], loggerFactory.CreateLogger<LspClientRegistry>());
        try
        {
            await registry.OpenDocumentAsync("file:///c:/proj/README.md", "# Heading", TestContext.Current.CancellationToken);
            await capture.DrainAsync();
            return (registry.Connections.Single(), capture.Snapshot(Id));
        }
        finally
        {
            try { await registry.DisposeAsync(); } catch { /* best effort */ }
            await capture.DisposeAsync();
        }
    }

    // ── the rerun ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task On_the_evidence_the_test_is_rerun_once_and_the_restart_is_announced_with_it()
    {
        var announced = new List<string>();
        var sut = new SilentServerRestart(2, announced.Add);
        var runs = 0;
        var resets = 0;

        await sut.RunAsync(
            () => ++runs == 1 ? throw new InvalidOperationException("no answer") : Task.CompletedTask,
            () => Task.FromResult<string?>("THE EVIDENCE"),
            () => { resets++; return Task.CompletedTask; });

        runs.Should().Be(2);
        resets.Should().Be(1);
        sut.Used.Should().Be(1);
        announced.Should().ContainSingle().Which.Should().Contain("#390").And.Contain("THE EVIDENCE");
    }

    [Fact]
    public async Task Without_the_evidence_the_failure_stands_and_nothing_is_rerun()
    {
        var sut = new SilentServerRestart(2, _ => { });
        var runs = 0;

        var act = () => sut.RunAsync(
            () => { runs++; throw new InvalidOperationException("a real failure"); },
            () => Task.FromResult<string?>(null),
            () => Task.CompletedTask);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("a real failure");
        runs.Should().Be(1);
        sut.Used.Should().Be(0);
    }

    [Fact]
    public async Task A_restart_over_the_cap_fails_the_test()
    {
        var sut = new SilentServerRestart(1, _ => { });
        var runs = 0;
        Task Body() => ++runs % 2 == 1 ? throw new InvalidOperationException("no answer") : Task.CompletedTask;

        await sut.RunAsync(Body, () => Task.FromResult<string?>("evidence"), () => Task.CompletedTask);
        var act = () => sut.RunAsync(Body, () => Task.FromResult<string?>("evidence"), () => Task.CompletedTask);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("over the cap of 1");
        runs.Should().Be(3, "the second test was not rerun");
    }

    [Fact]
    public async Task A_rerun_that_fails_too_carries_the_first_attempts_evidence()
    {
        var sut = new SilentServerRestart(2, _ => { });

        var act = () => sut.RunAsync(
            () => throw new InvalidOperationException("no answer"),
            () => Task.FromResult<string?>("THE FIRST RECORD"),
            () => Task.CompletedTask);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("THE FIRST RECORD");
    }
}
