using System.Text;
using HexIDE.Conversations;
using HexIDE.Redaction;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// The reply half of an exchange: kept, addressable, and exported.
/// </summary>
/// <remarks>
/// <b>It used to be none of those, and the omission cost the single most valuable frame in the record.</b>
/// A response completes its request's entry rather than making one of its own — correct for a timeline
/// whose job is to be read in order — but the bytes were then dropped on the floor. So no answer could be
/// read back at all: not a hover's contents, not an error object, and not the <c>InitializeResult</c> that
/// decides what every later message in the conversation is allowed to be. Reading a server's advertised
/// capabilities meant driving it from outside the IDE with a second client, which is precisely the work
/// the inspector exists to abolish (#429).
///
/// <para>
/// The number the answer is kept under is one it was already allocated. Responses have always consumed a
/// sequence — which is why a listing has always had gaps in it, and why the automation tool has always had
/// to explain those gaps — so naming it costs nothing and turns an apology into an address.
/// </para>
/// </remarks>
public class AnswerBodiesTests : IAsyncDisposable
{
    private readonly ConversationLog _log = new();

    public async ValueTask DisposeAsync()
    {
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private const string Question = """{"jsonrpc":"2.0","id":"1","method":"initialize","params":{}}""";
    private const string Answer =
        """{"jsonrpc":"2.0","id":"1","result":{"capabilities":{"diagnosticProvider":{}}}}""";

    /// <summary>
    /// Records an exchange the way the tap does, asking the arming gate exactly as it would.
    /// </summary>
    /// <remarks>
    /// The gate is consulted rather than bypassed, because a helper that always handed bytes over would
    /// exercise a path production never takes and every assertion about an unarmed connection would be
    /// testing nothing.
    /// </remarks>
    private void Exchange(
        string connectionId = "vb6",
        string question = Question,
        string? answer = Answer,
        ConversationEntryKind answerKind = ConversationEntryKind.Response)
    {
        var asked = Encoding.UTF8.GetBytes(question);
        _log.Record(
            connectionId, ConversationDirection.Sent, ConversationEntryKind.Request, "initialize", "1",
            asked.Length, _log.ShouldKeepBody(connectionId) ? asked : null);

        if (answer is null) return;

        var replied = Encoding.UTF8.GetBytes(answer);
        _log.Record(
            connectionId, ConversationDirection.Received, answerKind, null, "1",
            replied.Length, _log.ShouldKeepBody(connectionId) ? replied : null);
    }

    // ── Kept and addressable ─────────────────────────────────────────────────

    [Fact]
    public async Task TheAnswerIsKeptAndTheRequestSaysWhere()
    {
        _log.Arm("vb6", true);
        Exchange();
        await _log.DrainAsync();

        var entry = _log.Snapshot().Should().ContainSingle(
            "a request and its answer are one entry; only the bodies are two").Subject;

        entry.AnswerSequence.Should().NotBeNull("the answer is fetchable and the request is where you find it");

        var body = _log.Body("vb6", entry.AnswerSequence!.Value);
        Encoding.UTF8.GetString(body!.Head).Should().Be(Answer,
            "this is the frame that says what the server can do, and it is the one that used to be lost");
    }

    [Fact]
    public async Task TheAnswerKeepsTheSequenceItWasAlreadyAllocated()
    {
        // Not a new number and not a derived one. A response has always consumed a sequence, which is the
        // whole reason a listing has gaps; this makes that gap the address rather than an apology.
        _log.Arm("vb6", true);
        Exchange();
        await _log.DrainAsync();

        var entry = _log.Snapshot().Single();
        entry.AnswerSequence.Should().Be(entry.Sequence + 1,
            "the response was the very next thing the pump numbered");
    }

    [Fact]
    public async Task AnErrorObjectIsKeptToo()
    {
        // The case with the most to say and the least room to say it: an envelope can report that a
        // request failed, and only the body carries the code and the message that explain why.
        const string Failed = """{"jsonrpc":"2.0","id":"1","error":{"code":-32602,"message":"no"}}""";

        _log.Arm("vb6", true);
        Exchange(answer: Failed, answerKind: ConversationEntryKind.ErrorResponse);
        await _log.DrainAsync();

        var entry = _log.Snapshot().Single();
        entry.Outcome.Should().Be(ConversationOutcome.Failed);

        var body = _log.Body("vb6", entry.AnswerSequence!.Value);
        Encoding.UTF8.GetString(body!.Head).Should().Contain("-32602");
    }

    [Fact]
    public async Task AnAnswerNobodyKeptIsNotAdvertisedAsFetchable()
    {
        // A sequence that answers nothing is worse than no sequence: it reads as a body the caller has
        // failed to find, and sends them looking for a tool problem that is not there.
        //
        // The opening allowance is spent first, deliberately. A connection's first few frames are kept
        // whatever the arming says — a handshake cannot be captured retrospectively — so an unarmed
        // connection tested from cold would keep the very bodies this is asserting are absent.
        BurnTheOpeningAllowance();
        Exchange();
        await _log.DrainAsync();

        var entry = _log.Snapshot().Last();
        entry.Method.Should().Be("initialize");
        entry.Outcome.Should().Be(ConversationOutcome.Answered, "the exchange happened either way");
        entry.AnswerSequence.Should().BeNull("nothing was retained, so there is nothing to point at");
        entry.AnswerSizeBytes.Should().NotBeNull("the size is metadata and does not need arming");
    }

    /// <summary>Spends the frames a connection keeps unconditionally, so arming is what decides.</summary>
    private void BurnTheOpeningAllowance()
    {
        for (var i = 0; i < ConversationLog.OpeningFrames; i++)
        {
            var frame = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","method":"opening"}""");
            _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "opening",
                null, frame.Length, _log.ShouldKeepBody("vb6") ? frame : null);
        }
    }

    /// <summary>A real JSON frame of a chosen size, so an eviction can be provoked deterministically.</summary>
    private static string Padded(string prefix, char fill, int bytes) =>
        prefix + "\"" + new string(fill, bytes) + "\"}}";

    // ── The size, which is the tier that is always on ────────────────────────

    [Fact]
    public async Task TheAnswersSizeIsRecordedWithoutArmingAnything()
    {
        // Metadata rather than content, so it belongs to the tier that runs unarmed. Before this, an
        // unarmed connection could say a request was answered in 38ms and not whether the answer was four
        // kilobytes or empty — and those are different findings about the same latency.
        var log = new ConversationLog(new CaptureLimits(EnvelopeEntries: 50, PrologueEntries: 0));
        await using var _ = log;

        log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request, "initialize", "1", 60);
        log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response, null, "1", 4096);
        await log.DrainAsync();

        var entry = log.Snapshot().Single();
        entry.AnswerSizeBytes.Should().Be(4096);
        log.IsArmed("vb6").Should().BeFalse("nothing was armed, and the size arrived anyway");
    }

    // ── Reading it back ──────────────────────────────────────────────────────

    [Fact]
    public async Task FetchingAnAnswerSaysWhatItAnswersAndWithWhichMethod()
    {
        // A response carries no method on the wire, only an id. A view that left the method blank would
        // be faithful to the frame and useless to the reader, who is looking at it precisely to find out
        // what was answered.
        _log.Arm("vb6", true);
        Exchange();
        await _log.DrainAsync();

        var entry = _log.Snapshot().Single();
        var view = await CaptureQueries.FetchAsync(_log, "vb6", entry.AnswerSequence!.Value);

        view.Should().NotBeNull();
        view!.IsAnswer.Should().BeTrue();
        view.AnswerTo.Should().Be(entry.Sequence);
        view.Method.Should().Be("initialize", "borrowed from the request, because a reply has none");
        view.Head.Should().Contain("diagnosticProvider");
    }

    [Fact]
    public async Task AQuestionThatHasAgedOutStillHasItsAnswer()
    {
        // The two bodies are stored separately and evicted oldest-first, so the question goes before the
        // reply does. A reader who can no longer see what was asked can still see what came back, and the
        // pairing survives to tell them which is which.
        // 64 KiB is the floor these limits clamp to; asking for less gets 64 KiB and a note saying so.
        var limits = new CaptureLimits(
            PayloadBytesPerConnection: 64 * 1024, GlobalPayloadBytes: 1024 * 1024);
        var log = new ConversationLog(limits);
        await using var _ = log;
        log.Arm("vb6", true);

        // Sized so that exactly one eviction settles it: a large question, a small reply, and one later
        // frame that cannot fit until the question goes and fits comfortably once it has. Counting small
        // frames up to the ceiling instead would leave the answer one frame from eviction too, and a test
        // that passes by a hair is a test that will fail for an unrelated reason later.
        var asked = Encoding.UTF8.GetBytes(
            Padded("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"initialize\",\"params\":{\"pad\":", 'q', 40 * 1024));
        log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request, "initialize", "1",
            asked.Length, asked);
        var replied = Encoding.UTF8.GetBytes(Answer);
        log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response, null, "1",
            replied.Length, replied);
        await log.DrainAsync();

        var entry = log.Snapshot().Single(e => e.Method == "initialize");

        var later = Encoding.UTF8.GetBytes(
            Padded("{\"jsonrpc\":\"2.0\",\"method\":\"noise\",\"params\":{\"pad\":", 'n', 30 * 1024));
        log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "noise", null,
            later.Length, later);
        await log.DrainAsync();

        log.Body("vb6", entry.Sequence).Should().BeNull("the question was stored first, so it went first");

        var view = await CaptureQueries.FetchAsync(log, "vb6", entry.AnswerSequence!.Value);
        view.Should().NotBeNull("the reply is still held, and it is reachable on its own");
        view!.IsAnswer.Should().BeTrue();
        view.Method.Should().Be("initialize", "the request's envelope still describes it, body or no body");
    }

    [Fact]
    public async Task AnAnswerWhoseBodyHasGoneSaysSoRatherThanDenyingItExisted()
    {
        // "No envelope anywhere in the record" would be wrong twice over for a sequence the listing had
        // just handed out, and would send the reader looking for a capture bug instead of an eviction.
        var limits = new CaptureLimits(
            PayloadBytesPerConnection: 64 * 1024, GlobalPayloadBytes: 1024 * 1024);
        var log = new ConversationLog(limits);
        await using var _ = log;
        log.Arm("vb6", true);

        // 40 KiB asked and 20 KiB answered, against a 64 KiB ceiling.
        var asked = Encoding.UTF8.GetBytes(
            Padded("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"initialize\",\"params\":{\"pad\":", 'q', 40 * 1024));
        log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Request, "initialize", "1",
            asked.Length, asked);
        var replied = Encoding.UTF8.GetBytes(
            Padded("{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"result\":{\"pad\":", 'r', 20 * 1024));
        log.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response, null, "1",
            replied.Length, replied);
        await log.DrainAsync();

        var answer = log.Snapshot().Single().AnswerSequence!.Value;

        // 55 KiB, which cannot be made room for by dropping the question alone: both halves go, and the
        // envelope that names them stays. That is the state a reader reaches by leaving the inspector open.
        var enormous = Encoding.UTF8.GetBytes(
            Padded("{\"jsonrpc\":\"2.0\",\"method\":\"noise\",\"params\":{\"pad\":", 'n', 55 * 1024));
        log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "noise", null,
            enormous.Length, enormous);
        await log.DrainAsync();

        log.Body("vb6", answer).Should().BeNull("the premise: the reply's body is gone");

        var explained = await CaptureQueries.ExplainMissingBodyAsync(log, "vb6", answer);
        explained.Should().Contain("is the answer to request")
            .And.Contain("initialize", "naming the request is what turns a dead end into an answer")
            .And.NotContain("may never have existed");
    }

    // ── Out through the export ───────────────────────────────────────────────

    [Fact]
    public async Task TheExportCarriesTheReplyOnItsOwnLine()
    {
        // The format's entire claim is that it replays into a client. A conversation with every reply
        // missing replays into nothing: the requests would simply hang.
        _log.Arm("vb6", true);
        Exchange();
        await _log.DrainAsync();

        var export = await ConversationExporter.ExportAsync(
            _log, new ConversationRedactor(new Pseudonymiser(new Random(7)), pseudonymise: true));

        var lines = export.Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2, "one entry, two frames");
        lines[0].Should().Contain("\"method\":\"initialize\"");
        lines[1].Should().Contain("diagnosticProvider", "the reply is the half that says what the server does");
    }

    [Fact]
    public async Task TheManifestNamesTheReplyAsAReplyAndNotAsAnEntry()
    {
        _log.Arm("vb6", true);
        Exchange();
        await _log.DrainAsync();

        var export = await ConversationExporter.ExportAsync(
            _log, new ConversationRedactor(new Pseudonymiser(new Random(7)), pseudonymise: true));

        using var manifest = System.Text.Json.JsonDocument.Parse(export.Manifest);
        var rows = manifest.RootElement.GetProperty("envelopes").EnumerateArray().ToArray();

        rows.Should().HaveCount(2);
        rows[0].TryGetProperty("answerTo", out _).Should().BeFalse("the request is not an answer to anything");

        var reply = rows[1];
        reply.GetProperty("answerTo").GetInt64().Should().Be(rows[0].GetProperty("sequence").GetInt64());
        reply.GetProperty("kind").GetString().Should().Be(nameof(ConversationEntryKind.Response));
        reply.GetProperty("direction").GetString().Should().Be(nameof(ConversationDirection.Received),
            "a reply travels the other way, and a file that said otherwise would lie about who spoke");
        reply.GetProperty("method").GetString().Should().Be("initialize",
            "borrowed from the request, because that is the only thing that identifies it");
    }

    [Fact]
    public async Task TheDisclosureCountsWhatTheReplyWillPutInSomebodyElsesHands()
    {
        // A disclosure that understates is the only kind that does harm, and answers now go out too.
        _log.Arm("vb6", true);
        Exchange();
        await _log.DrainAsync();

        var disclosure = await ConversationDisclosure.OfAsync(_log);

        disclosure.Messages.Should().Be(2, "the reply is a line in the file and is counted as one");
        disclosure.RetainedBytes.Should().BeGreaterThanOrEqualTo(Question.Length + Answer.Length);
    }
}
