using System.Text;
using HexIDE.Conversations;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// The service a tap talks to, and the only place a conversation is assembled.
/// </summary>
public class ConversationLogTests : IAsyncDisposable
{
    private readonly ConversationLog _log = new();

    public async ValueTask DisposeAsync()
    {
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static byte[] Body(string text) => Encoding.UTF8.GetBytes(text);

    // ── Pairing, which is where the measured hazard lives ────────────────────

    [Fact]
    public async Task AResponseCompletesItsRequestRatherThanAddingASecondEntry()
    {
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request, "textDocument/hover", "1", 120);
        _log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response, "textDocument/hover", "1", 400);
        await _log.DrainAsync();

        var entries = _log.Snapshot();
        entries.Should().ContainSingle("a request and its answer are one exchange, not two lines");
        entries[0].Outcome.Should().Be(ConversationOutcome.Answered);
        entries[0].Elapsed.Should().NotBeNull("a duration is most of what a server author wants");
    }

    [Fact]
    public async Task AnIdOnOneSideDoesNotAnswerTheSameIdOnTheOther()
    {
        // THE assertion of this class, and it is measured rather than imagined: a server-initiated request
        // numbered 2 was observed arriving while this client's own outbound request 2 was still open.
        // Pairing on the id alone merges two entirely unrelated messages, and the merged entry then carries
        // a duration computed between two things that never had anything to do with each other.
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request, "textDocument/hover", "2", 100);
        _log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Request, "workspace/configuration", "2", 90);
        await _log.DrainAsync();

        // Our reply to THEIR request must close theirs, and leave ours open.
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Response, "workspace/configuration", "2", 50);
        await _log.DrainAsync();

        var entries = _log.Snapshot();
        entries.Should().HaveCount(2, "two requests were sent and only one has been answered");

        var ours = entries.Single(e => e.Method == "textDocument/hover");
        var theirs = entries.Single(e => e.Method == "workspace/configuration");
        ours.Outcome.Should().Be(ConversationOutcome.None, "nothing has answered our hover yet");
        theirs.Outcome.Should().Be(ConversationOutcome.Answered);
    }

    [Fact]
    public async Task AnErrorReplyIsAFailureRatherThanAnAnswer()
    {
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request, "textDocument/rename", "5", 100);
        _log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.ErrorResponse, "textDocument/rename", "5", 80);
        await _log.DrainAsync();

        _log.Snapshot()[0].Outcome.Should().Be(ConversationOutcome.Failed);
    }

    [Fact]
    public async Task ARequestThatNeverComesBackStaysVisible()
    {
        // The absence IS the finding. A record that only wrote an entry once a reply arrived would show
        // nothing at all for the one failure mode nobody can otherwise see.
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request, "textDocument/hover", "9", 100);
        await _log.DrainAsync();

        var entry = _log.Snapshot().Single();
        entry.Outcome.Should().Be(ConversationOutcome.None);
        entry.Elapsed.Should().BeNull();
    }

    // ── Ordering across connections ──────────────────────────────────────────

    [Fact]
    public async Task OneTimelineOrdersEveryConnectionTogether()
    {
        // Ordered by sequence rather than timestamp: connections are recorded independently and a clock has
        // finite resolution, so two frames in the same tick would tie and a merged view would be arbitrary.
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "a", null, 1);
        _log.Record("latex", ConversationDirection.Sent, ConversationEntryKind.Notification, "b", null, 1);
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "c", null, 1);
        await _log.DrainAsync();

        _log.Snapshot().Select(e => e.Method).Should().Equal("a", "b", "c");
        _log.Snapshot().Select(e => e.Sequence).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task OneConnectionCanBeReadOnItsOwn()
    {
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "a", null, 1);
        _log.Record("latex", ConversationDirection.Sent, ConversationEntryKind.Notification, "b", null, 1);
        await _log.DrainAsync();

        _log.Snapshot("latex").Select(e => e.Method).Should().Equal("b");
    }

    // ── Arming, which is the whole disclosure boundary ───────────────────────

    [Fact]
    public async Task NothingIsRetainedUntilAConnectionIsArmed()
    {
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification,
            "textDocument/didChange", null, 2048, body: null);
        await _log.DrainAsync();

        var entry = _log.Snapshot().Single();
        entry.SizeBytes.Should().Be(2048, "the size is metadata and is always recorded");
        _log.Body("vb6", entry.Sequence).Should().BeNull("no body was handed over, because nothing was armed");
    }

    [Fact]
    public async Task AnArmedConnectionKeepsWhatItWasGiven()
    {
        _log.Arm("vb6", true);
        _log.IsArmed("vb6").Should().BeTrue();

        var body = Body("""{"jsonrpc":"2.0","method":"textDocument/didChange"}""");
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification,
            "textDocument/didChange", null, body.Length, body);
        await _log.DrainAsync();

        var entry = _log.Snapshot().Single();
        _log.Body("vb6", entry.Sequence).Should().NotBeNull();
    }

    [Fact]
    public void ArmingIsPerConnection()
    {
        _log.Arm("vb6", true);

        _log.IsArmed("vb6").Should().BeTrue();
        _log.IsArmed("latex").Should().BeFalse("arming one server must not arm the rest");
    }

    // ── The things that are not messages ─────────────────────────────────────

    [Fact]
    public async Task WhatWasNeverSentIsRecordedToo()
    {
        // A refusal is invisible and looks exactly like a broken feature. This is the entry that turns
        // "nothing happened when I pressed F12" into "we did not ask, and here is why".
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.NeverSent,
            "textDocument/definition", null, 0, detail: "definitionProvider was not advertised");
        _log.Record("vb6", ConversationDirection.Local, ConversationEntryKind.Unconsumed,
            "textDocument/codeAction", null, 0, detail: "advertised, and this client does not use it");
        _log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.StandardError,
            null, null, 40, detail: "clangd: no compile_commands.json found");
        _log.Record("vb6", ConversationDirection.Local, ConversationEntryKind.Lifecycle,
            null, null, 0, detail: "exited with code 1");
        await _log.DrainAsync();

        _log.Snapshot().Select(e => e.Kind).Should().Equal(
            ConversationEntryKind.NeverSent,
            ConversationEntryKind.Unconsumed,
            ConversationEntryKind.StandardError,
            ConversationEntryKind.Lifecycle);
    }

    // ── Losing things, audibly ───────────────────────────────────────────────

    [Fact]
    public async Task AQueueThatCannotKeepUpSaysSo()
    {
        // The counter this exercises could not work at all with the obvious channel setting: `DropWrite`
        // discards the item and reports success, so a drop count built on the return value stays at zero
        // forever. `Wait` plus `TryWrite` is the combination that never blocks AND reports.
        await using var tiny = new ConversationLog(queueDepth: 1);

        for (var i = 0; i < 20_000; i++)
        {
            tiny.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "spam", null, 1);
        }

        tiny.QueueDropped.Should().BeGreaterThan(0,
            "twenty thousand writes into a queue of one cannot all have been processed in between");
    }

    [Fact]
    public async Task DrainingWaitsForWorkRatherThanForAnEmptyQueue()
    {
        // A queue length of zero proves nothing: an item can be out of the queue and still being processed.
        // Everything else in this class depends on this being a fence rather than a poll.
        for (var i = 0; i < 500; i++)
        {
            _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "n", null, 1);
        }

        await _log.DrainAsync();

        _log.Snapshot().Should().HaveCount(500);
    }
}
