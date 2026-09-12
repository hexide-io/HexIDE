using HexIDE.Conversations;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// The copy shape, which is the one artefact here addressed to somebody who does not have HexIDE.
/// </summary>
/// <remarks>
/// <b>These assert the borrowed vocabulary literally, and that is the point rather than brittleness.</b>
/// The whole value of copying Visual Studio Code's trace lines is that a server author recognises them on
/// sight; a line that drifts into "Sent request" or drops the millisecond is no longer recognisable, and
/// nothing else in the build would notice. The words are the contract.
/// </remarks>
public class ConversationTraceTests
{
    private static ConversationEnvelope Envelope(
        ConversationDirection direction,
        ConversationEntryKind kind,
        string? method = "textDocument/hover",
        string? correlationId = "7",
        TimeSpan? elapsed = null,
        string? detail = null) =>
        new(
            Sequence: 3,
            ConnectionId: "hexide.vb6",
            Timestamp: new DateTimeOffset(2026, 9, 12, 1, 33, 30, 727, TimeSpan.Zero),
            Direction: direction,
            Kind: kind,
            Method: method,
            CorrelationId: correlationId,
            SizeBytes: 120,
            Elapsed: elapsed,
            Detail: detail);

    [Fact]
    public void ARequestReadsTheWayAServerAuthorsOwnTraceReads()
    {
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Sent, ConversationEntryKind.Request),
            """{"jsonrpc":"2.0","id":7}""");

        text.Should().Contain("Sending request 'textDocument/hover - (7)'.");
        text.Should().Contain("Params:");
    }

    [Fact]
    public void AResponseCarriesItsLatency()
    {
        // For an author this is the difference between a bug report and an anecdote, and the borrowed
        // shape already has a place for it.
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Received, ConversationEntryKind.Response,
                     elapsed: TimeSpan.FromMilliseconds(35)),
            """{"jsonrpc":"2.0","id":7,"result":null}""");

        text.Should().Contain("Received response 'textDocument/hover - (7)' in 35ms.");
        text.Should().Contain("Result:");
    }

    [Fact]
    public void ARequestThatNeverCameBackReportsNoLatencyRatherThanZero()
    {
        // A reported "in 0ms" would be a claim, and the claim would be false. The absence is the finding.
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Sent, ConversationEntryKind.Request), null,
            "Envelope 3 exists and has no retained body.");

        text.Should().NotContain("ms.");
        text.Should().Contain("No body: Envelope 3 exists and has no retained body.",
            "a trace whose payload is simply missing reads as a message that carried nothing");
    }

    [Fact]
    public void AnErrorResponseLabelsItsPayloadAsAnError()
    {
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Received, ConversationEntryKind.ErrorResponse,
                     elapsed: TimeSpan.FromMilliseconds(4)),
            """{"jsonrpc":"2.0","id":7,"error":{"code":-32601}}""");

        text.Should().Contain("Error:");
        text.Should().NotContain("Result:");
    }

    [Fact]
    public void ANotificationHasNoIdBecauseItHasNoAnswer()
    {
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Sent, ConversationEntryKind.Notification,
                     method: "textDocument/didOpen", correlationId: null),
            """{"jsonrpc":"2.0"}""");

        text.Should().Contain("Sending notification 'textDocument/didOpen'.");
        text.Should().NotContain(" - (");
    }

    [Fact]
    public void TheServerIsNamedOnTheLine()
    {
        // The one deviation from the borrowed shape, and it is forced: Visual Studio Code names the server
        // in the output channel, while this window shows every server on one timeline. A line with no
        // server on it is ambiguous the moment two are attached, which is the case this exists for.
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Sent, ConversationEntryKind.Request), null);

        text.Should().StartWith("[Trace - 0").And.Contain("[hexide.vb6]");
    }

    [Fact]
    public void AnEntryThatNeverCrossedTheWireIsNotDressedAsProtocol()
    {
        // A lifecycle note rendered as "Sending notification" would be a lie about the protocol, inside a
        // document whose whole purpose is to be quoted at the person who wrote the server.
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Local, ConversationEntryKind.Lifecycle,
                     method: null, correlationId: null, detail: "process exited with 1"),
            null);

        text.Should().Contain("Process: process exited with 1.");
        text.Should().NotContain("notification");
        text.Should().NotContain("request");
    }

    [Fact]
    public void ACapabilityWeDeclinedToUseSaysWhichOne()
    {
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Local, ConversationEntryKind.NeverSent,
                     method: "textDocument/rename", correlationId: null,
                     detail: "the server advertised no renameProvider"),
            null);

        text.Should().Contain("Did not send 'textDocument/rename'");
        text.Should().Contain("no renameProvider");
    }

    [Fact]
    public void ABodyThisClientCouldNotDecodeIsStillShown()
    {
        // Exactly when a trace earns its keep. Reformatting must never become a reason a body cannot be
        // seen, so a parse failure falls back to the bytes rather than to nothing.
        var text = ConversationTrace.ToTraceText(
            Envelope(ConversationDirection.Received, ConversationEntryKind.Response),
            "{this is not json");

        text.Should().Contain("{this is not json");
    }

    [Fact]
    public void SeveralMessagesAreSeparatedTheWayARealTraceSeparatesThem()
    {
        var text = ConversationTrace.ToTraceText(
        [
            (Envelope(ConversationDirection.Sent, ConversationEntryKind.Request), """{"a":1}"""),
            (Envelope(ConversationDirection.Received, ConversationEntryKind.Response,
                      elapsed: TimeSpan.FromMilliseconds(2)), """{"b":2}"""),
        ]);

        text.Should().Contain("Sending request").And.Contain("Received response");
        text.Should().Contain("\n\n[Trace", "a blank line between blocks is what makes a trace scannable");
    }
}
