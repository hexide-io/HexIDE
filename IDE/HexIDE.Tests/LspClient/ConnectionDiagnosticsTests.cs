using System.Text;
using HexIDE.Conversations;
using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A guard for the guard.
/// </summary>
/// <remarks>
/// <b>The thing this explains only ever runs when something has already gone wrong on a machine nobody is
/// watching.</b> So there is no ordinary run in which anyone would notice it had quietly started returning
/// nothing — a renamed field, a record member that stopped being populated, an exception swallowed
/// somewhere. It would read as an assertion with an empty reason, which is exactly where this started
/// (hexide-io/HexIDE#390).
///
/// <para>
/// These drive it against a connection built by hand rather than a real server, because what is being
/// checked is that it says the things, not that a server does them.
/// </para>
/// </remarks>
public class ConnectionDiagnosticsTests
{
    private static LanguageServerConnection Failed(LanguageConnectionAttempt? attempt) => new(
        Id: "foreign.markdown",
        DisplayName: "Foreign Markdown server",
        Kind: LanguageConnectionKind.LanguageServer,
        State: LanguageConnectionState.Failed,
        Extensions: [".md"],
        LanguageId: "markdown",
        Capabilities: null,
        Attempt: attempt);

    private static LanguageConnectionAttempt AnAttempt() => new(
        ReachedStage: LanguageConnectionStage.HandshakeSent,
        Steps:
        [
            new LanguageConnectionStep(
                LanguageConnectionStage.Connecting, LanguageConnectionStepOutcome.Reached,
                TimeSpan.Zero, null),
            new LanguageConnectionStep(
                LanguageConnectionStage.HandshakeSent, LanguageConnectionStepOutcome.StoppedHere,
                TimeSpan.FromSeconds(30), "the server did not answer initialize within 0:00:30"),
        ],
        StartedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public void ItNamesTheStageAndRepeatsWhatTheClientRecorded()
    {
        var explained = ConnectionDiagnostics.Explain(Failed(AnAttempt()));

        explained.Should().Contain("foreign.markdown").And.Contain("Failed");
        explained.Should().Contain("HandshakeSent", "the stage reached is the first question asked");
        explained.Should().Contain("did not answer initialize",
            "the client already writes the answer into the attempt; the only failure was not reading it");
        explained.Should().Contain("30000ms", "when it gave up is how a timeout is told from a crash");
    }

    [Fact]
    public void AConnectionWithNoAttemptSaysSoRatherThanSayingNothing()
    {
        // The shape that would otherwise produce an empty reason, which is indistinguishable from the
        // diagnostic having broken.
        ConnectionDiagnostics.Explain(Failed(attempt: null))
            .Should().Contain("no attempt at all");
    }

    [Fact]
    public async Task TheCapturedWireIsIncludedWhenThereIsOne()
    {
        await using var capture = new ConversationLog();
        var body = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""");

        capture.Record("foreign.markdown", ConversationDirection.Local, ConversationEntryKind.Lifecycle,
            null, null, 0, null, "connected");
        capture.Record("foreign.markdown", ConversationDirection.Sent, ConversationEntryKind.Request,
            "initialize", "1", body.Length, body);

        var explained = ConnectionDiagnostics.Explain(Failed(AnAttempt()), capture);

        explained.Should().Contain("initialize");
        explained.Should().Contain("connected", "process lifecycle is the half the attempt cannot see");
    }

    [Fact]
    public async Task ItDrainsBeforeReadingSoTheLastThingThatHappenedIsNotMissing()
    {
        // The envelope still in the queue is precisely the one that would explain a failure. Recorded and
        // read back immediately, with no drain of its own, so a missing drain inside Explain shows up here.
        await using var capture = new ConversationLog();

        for (var i = 0; i < 50; i++)
        {
            capture.Record("foreign.markdown", ConversationDirection.Received,
                ConversationEntryKind.Lifecycle, null, null, 0, null, $"stderr line {i}");
        }

        ConnectionDiagnostics.Explain(Failed(AnAttempt()), capture)
            .Should().Contain("stderr line 49");
    }

    [Fact]
    public async Task ACaptureThatRecordedNothingSaysThatToo()
    {
        await using var capture = new ConversationLog();

        ConnectionDiagnostics.Explain(Failed(AnAttempt()), capture)
            .Should().Contain("recorded nothing for this connection",
                "silence from the capture is a finding, not an absence of one");
    }
}
