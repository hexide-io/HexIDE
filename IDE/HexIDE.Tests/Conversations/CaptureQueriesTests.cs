using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Redaction;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// Reading a capture back, driven through a real JSON-RPC connection rather than a hand-built record.
/// </summary>
/// <remarks>
/// <b>Every test here provokes traffic and then asks about it, because that is the question the automation
/// tools exist to answer.</b> A test that recorded envelopes by calling <c>Record</c> directly would prove
/// the filters work and would miss the one defect this surface is most likely to have: the tap hands frames
/// to a channel and never waits, so a listing taken without draining is short by exactly the frames the
/// caller has just caused. Building the record by hand hides that, because a hand-built record has nothing
/// in flight.
///
/// <para>
/// This is also where task 3.4's weight sits. The tools themselves are four wrappers around this surface,
/// in an executable nothing references and therefore no test can reach — so the logic lives here and is
/// driven here, and the live automation check is about the wiring rather than the behaviour.
/// </para>
/// </remarks>
public class CaptureQueriesTests : IAsyncDisposable
{
    private readonly ConversationLog _log = new();
    private readonly List<IDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables) { try { d.Dispose(); } catch { /* teardown */ } }
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private sealed class Server
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{"hoverProvider":true}}""").RootElement.Clone();

        [JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Hover(JsonElement _) => JsonDocument.Parse("null").RootElement.Clone();

        [JsonRpcMethod("boom")]
        public int Boom() => throw new InvalidOperationException("the server refused");

        [JsonRpcMethod("note")]
        public void Note(string _) { }
    }

    private JsonRpc Connect(string connectionId)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var peer = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            new Server());
        peer.StartListening();

        var tapped = new CapturingFormatter(new SystemTextJsonFormatter(), _log, connectionId);
        var client = new JsonRpc(new HeaderDelimitedMessageHandler(clientSide, clientSide, tapped));
        client.StartListening();

        _disposables.Add(peer);
        _disposables.Add(client);
        return client;
    }

    /// <summary>A handshake and a hover, as an ordinary session begins.</summary>
    private async Task<JsonRpc> AConversation(string connectionId = "vb6")
    {
        var client = Connect(connectionId);

        await client.InvokeWithParameterObjectAsync<JsonElement>(
            "initialize", new { processId = 1 }, TestContext.Current.CancellationToken);
        await client.InvokeWithParameterObjectAsync<JsonElement>(
            "textDocument/hover", new { line = 1 }, TestContext.Current.CancellationToken);

        return client;
    }

    // ── Listing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhatJustHappenedIsVisible()
    {
        await AConversation();

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());

        page.Entries.Should().HaveCount(2, "one exchange each for initialize and hover");
        page.Entries.Select(e => e.Method).Should().Equal(["initialize", "textDocument/hover"]);
    }

    [Fact]
    public void EveryReaderDrainsBeforeItReads()
    {
        // THE guarantee, asserted structurally because it cannot honestly be asserted behaviourally.
        //
        // MEASURED, AND THIS IS THE THIRD ATTEMPT. A behavioural version was written three ways — two
        // round trips, a thousand frames, a dozen frames — and each one passed with the drain REMOVED,
        // because whether the pump happens to be behind at the moment you look is a race that usually
        // resolves the convenient way. One shape did fail once, which is worse than never failing: it
        // would have shipped as a guard that fires on CI and nowhere else.
        //
        // So the promise is checked where it is made. Removing a drain fails this immediately, on every
        // machine. The behavioural tests around it prove the data is right; this proves nothing can read
        // the record without asking the pump first.
        var source = File.ReadAllText(SourceOfCaptureQueries());

        var entryPoints = Regex.Matches(source, @"public static async Task[^\n]*?\b(?<name>\w+)\(")
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        entryPoints.Should().NotBeEmpty(
            "finding none would mean the pattern stopped matching, and a guard that checks nothing passes "
          + "for the wrong reason");
        entryPoints.Should().HaveCountGreaterThanOrEqualTo(3, "there are three ways to read the record");

        foreach (var name in entryPoints)
        {
            BodyOf(source, name).Should().Contain("DrainAsync",
                "{0} reads the record, and a read that has not drained is short by exactly the frames the "
              + "caller just provoked — which are the only ones it is asking about", name);
        }
    }

    private static string SourceOfCaptureQueries()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        var path = Path.Combine(
            dir!.FullName, "IDE", "HexIDE.Core", "Conversations", "CaptureQueries.cs");
        File.Exists(path).Should().BeTrue("this guard reads the query surface's own source");
        return path;
    }

    /// <summary>The text of one method, from its opening brace to the matching close.</summary>
    private static string BodyOf(string source, string method)
    {
        var at = source.IndexOf($" {method}(", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, "{0} should be findable in the source", method);

        var open = source.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..i];
        }

        return source[open..];
    }

    // ── Saying why, when the answer would otherwise need guessing at ─────────
    //
    // The automation surface ships to developers driving it with models nobody here chooses, so a reply
    // that forces a guess is a defect in the same way a bad dialog is. These pin the four states a bare
    // count of zero collapses — and the first is the one I hit myself and worked around without noticing,
    // which is exactly what a first-time caller cannot do.

    [Fact]
    public async Task AnEmptyRecordSaysWhyItIsEmpty()
    {
        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());

        page.Note.Should().NotBeNull();
        page.Note.Should().Contain("first document",
            "a language server starts lazily, so an empty record is the normal state before anything is "
          + "opened — and the commonest reason a caller sees nothing");
    }

    [Fact]
    public async Task AConnectionIdNobodyHasSaysWhichOnesExist()
    {
        await AConversation("vb6");

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(ConnectionId: "vb"));

        page.Note.Should().Contain("vb6", "a misspelled id should name the real ones rather than look empty");
    }

    [Fact]
    public async Task AClearedRecordIsNotMistakenForAServerThatNeverRan()
    {
        await AConversation("vb6");
        await _log.ClearAsync();

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());

        page.Note.Should().Contain("cleared");
        page.Note.Should().NotContain("first document",
            "the server did run; saying it has not started would send a caller to open a file that is "
          + "already open");
    }

    [Fact]
    public async Task AFilterThatExcludedEverythingSaysThatRatherThanLookingEmpty()
    {
        await AConversation("vb6");

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Method: "textDocument/rename"));

        page.Note.Should().Contain("none match this filter");
    }

    [Fact]
    public async Task AnAnswerWithContentCarriesNoNote()
    {
        // The note is for an answer that needs explaining. On every ordinary reply it would be noise, and
        // a field that is always populated stops being read.
        await AConversation();

        (await CaptureQueries.ListAsync(_log, new EnvelopeFilter())).Note.Should().BeNull();
    }

    [Fact]
    public async Task ASequenceOnTheWrongConnectionSaysWhereItActuallyIs()
    {
        // Sequence numbers are unique across the record rather than per connection, so naming the wrong
        // connection for a real sequence is the likeliest mistake available. A dead end here would send a
        // caller looking for a message that is sitting right there.
        _log.Arm("vb6", true);
        _log.Arm("latex", true);
        await AConversation("vb6");
        await AConversation("latex");

        var onLatex = (await CaptureQueries.ListAsync(_log, new EnvelopeFilter(ConnectionId: "latex")))
            .Entries[0].Sequence;

        var explained = await CaptureQueries.ExplainMissingBodyAsync(_log, "vb6", onLatex);

        explained.Should().Contain("belongs to connection 'latex'");
    }

    [Fact]
    public async Task AMethodFilterAnswersWhetherSomethingWasEverSent()
    {
        await AConversation();

        var asked = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Method: "textDocument/hover"));
        var never = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Method: "textDocument/rename"));

        asked.Entries.Should().ContainSingle();
        never.Entries.Should().BeEmpty(
            "'it was never sent' and 'it came back empty' are different findings and only one is a defect "
          + "in the editor");
    }

    [Fact]
    public async Task AMethodFilterIsExactRatherThanAPrefix()
    {
        await AConversation();

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Method: "textDocument/hov"));

        page.Entries.Should().BeEmpty("a method name is an identifier on the wire, not a search term");
    }

    [Fact]
    public async Task FailuresCanBeAskedForOnTheirOwn()
    {
        var client = await AConversation();
        var boom = async () => await client.InvokeAsync<int>("boom");
        await boom.Should().ThrowAsync<RemoteInvocationException>();

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(FailuresOnly: true));

        page.Entries.Should().ContainSingle("one request failed and two did not");
        page.Entries[0].Method.Should().Be("boom");
    }

    [Fact]
    public async Task PollingForWhatIsNewReturnsOnlyThat()
    {
        var client = await AConversation();
        var soFar = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
        var latest = soFar.Entries[^1].Sequence;

        await client.InvokeWithParameterObjectAsync<JsonElement>(
            "textDocument/hover", new { line = 2 }, TestContext.Current.CancellationToken);

        var since = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: latest));

        since.Entries.Should().ContainSingle(
            "exercising one thing and asking what is new is the cheap loop this exists for");
    }

    [Fact]
    public async Task TheNewestAreKeptWhenThereAreMoreThanAskedFor()
    {
        // A client that has just done something is asking about the end of the conversation. Keeping the
        // oldest would hand it the handshake every time and never the thing it provoked.
        var client = Connect("vb6");
        for (var i = 0; i < 12; i++) await client.NotifyAsync("note", $"n{i}");

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Limit: 3));

        page.Entries.Should().HaveCount(3);
        page.Matched.Should().Be(12, "a limited answer has to say what it was limited from");
        page.Truncated.Should().BeTrue();
        page.Entries.Select(e => e.Sequence).Should().BeInAscendingOrder(
            "however few come back, they come back in the order they happened");
        page.Entries[^1].Sequence.Should().Be(
            (await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Limit: int.MaxValue))).Entries[^1].Sequence);
    }

    [Fact]
    public async Task AnUntruncatedAnswerSaysSo()
    {
        await AConversation();

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Limit: 50));

        page.Truncated.Should().BeFalse();
        page.Matched.Should().Be(page.Entries.Count);
    }

    [Fact]
    public async Task OneConnectionCanBeAskedAboutAlone()
    {
        await AConversation("vb6");
        await AConversation("latex");

        var vb6 = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(ConnectionId: "vb6"));
        var all = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());

        vb6.Entries.Should().OnlyContain(e => e.ConnectionId == "vb6");
        all.Entries.Should().HaveCount(4, "one timeline, and both connections are in it");
    }

    // ── Fetching ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task OneBodyComesBackAsTheBytesThatCrossedTheWire()
    {
        _log.Arm("vb6", true);
        await AConversation();

        var hover = (await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Method: "textDocument/hover")))
            .Entries.Single();

        var body = await CaptureQueries.FetchAsync(_log, "vb6", hover.Sequence);

        body.Should().NotBeNull();
        body!.Head.Should().Contain("\"method\":\"textDocument/hover\"");
        body.Method.Should().Be("textDocument/hover");
        body.TrueLength.Should().BeGreaterThan(0);
        body.Redacted.Should().BeFalse("the live answer is raw, and export is where redaction belongs");
    }

    [Fact]
    public async Task ABodyCanBeAskedForRedacted()
    {
        _log.Arm("vb6", true);
        var client = Connect("vb6");
        await client.NotifyAsync("note", "file:///C:/Users/quintana/Ledger/Form1.frm");

        var note = (await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Method: "note"))).Entries.Single();
        var body = await CaptureQueries.FetchAsync(
            _log, "vb6", note.Sequence, new ConversationRedactor(new Pseudonymiser()));

        body.Should().NotBeNull();
        body!.Head.Should().NotContain("quintana").And.NotContain("Ledger");
        body.Redacted.Should().BeTrue("anything that leaves has to say which of the two it is");
    }

    [Fact]
    public async Task AnUnarmedConnectionKeepsNoBodyPastItsOpening()
    {
        var client = Connect("vb6");
        for (var i = 0; i < ConversationLog.OpeningFrames + 3; i++) await client.NotifyAsync("note", $"n{i}");

        var last = (await CaptureQueries.ListAsync(_log, new EnvelopeFilter())).Entries[^1];

        (await CaptureQueries.FetchAsync(_log, "vb6", last.Sequence)).Should().BeNull();
    }

    [Fact]
    public async Task AMissingBodyIsExplainedRatherThanLeftAsAnAbsence()
    {
        // Three states a single word would collapse: no such envelope, nothing was kept, what was kept has
        // gone. The record cannot attribute one envelope to the middle two, and says so rather than
        // guessing — which is the honest version of an answer an agent would otherwise over-read.
        var client = Connect("vb6");
        for (var i = 0; i < ConversationLog.OpeningFrames + 3; i++) await client.NotifyAsync("note", $"n{i}");

        var last = (await CaptureQueries.ListAsync(_log, new EnvelopeFilter())).Entries[^1];

        var known = await CaptureQueries.ExplainMissingBodyAsync(_log, "vb6", last.Sequence);
        known.Should().Contain("not armed").And.Contain("exists");

        var unknown = await CaptureQueries.ExplainMissingBodyAsync(_log, "vb6", 999_999);
        unknown.Should().Contain("never have existed");
    }

    // ── Arming and clearing ──────────────────────────────────────────────────

    [Fact]
    public async Task ArmingEveryConnectionAtConstructionReachesTheHandshake()
    {
        // What the launch flag buys, and the reason it cannot be a loop over configured ids: this
        // connection was never named to anybody before it appeared.
        await using var armed = new ConversationLog(armEveryConnection: true);
        armed.ArmsEveryConnection.Should().BeTrue();

        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var peer = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            new Server());
        peer.StartListening();
        _disposables.Add(peer);

        var client = new JsonRpc(new HeaderDelimitedMessageHandler(
            clientSide, clientSide, new CapturingFormatter(new SystemTextJsonFormatter(), armed, "late")));
        client.StartListening();
        _disposables.Add(client);

        for (var i = 0; i < ConversationLog.OpeningFrames + 3; i++) await client.NotifyAsync("note", $"n{i}");

        var last = (await CaptureQueries.ListAsync(armed, new EnvelopeFilter())).Entries[^1];

        (await CaptureQueries.FetchAsync(armed, "late", last.Sequence)).Should().NotBeNull(
            "past the opening allowance, only arming keeps a body — and nothing armed this by name");
        armed.IsArmed("late").Should().BeTrue();
    }

    [Fact]
    public async Task ClearingEmptiesTheRecordAndLeavesTheArmingAlone()
    {
        _log.Arm("vb6", true);
        await AConversation();

        var discarded = await _log.ClearAsync("vb6");

        discarded.Should().Be(2, "a clear says what it did");
        (await CaptureQueries.ListAsync(_log, new EnvelopeFilter())).Entries.Should().BeEmpty();
        _log.IsArmed("vb6").Should().BeTrue(
            "clearing throws away what has been watched, not the decision to watch");
    }

    [Fact]
    public async Task RecordingContinuesAfterAClear()
    {
        // The loop this exists for: exercise, read, clear, exercise the next thing. A clear that stopped
        // the recording would make the second half of that silently useless.
        _log.Arm("vb6", true);
        var client = await AConversation();
        await _log.ClearAsync("vb6");

        await client.InvokeWithParameterObjectAsync<JsonElement>(
            "textDocument/hover", new { line = 9 }, TestContext.Current.CancellationToken);

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
        page.Entries.Should().ContainSingle();
        (await CaptureQueries.FetchAsync(_log, "vb6", page.Entries[0].Sequence)).Should().NotBeNull();
    }

    [Fact]
    public async Task AClearedConnectionIsStillAConnection()
    {
        // MEASURED ON THE FIRST REAL USE OF THE TOOLS, and it defeated the thing it was built for. The
        // state an arm or clear reports used to derive its connection list from the envelopes present,
        // which is the same answer until somebody clears — and then it says there are no connections at
        // all, while they are alive and armed.
        //
        // The state is returned by those tools precisely so arming is not invisible: an agent that cleared
        // a misspelled id and one that cleared a real id were getting the identical empty reply.
        _log.Arm("vb6", true);
        await AConversation();

        await _log.ClearAsync("vb6");

        _log.ConnectionIds.Should().Contain("vb6",
            "the connection did not go anywhere; only what was recorded about it did");
        _log.IsArmed("vb6").Should().BeTrue();
        (await CaptureQueries.ListAsync(_log, new EnvelopeFilter())).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AConnectionIsKnownBeforeItHasSaidAnything()
    {
        // Arming by name creates the entry, which is what lets a launch flag or an agent arm a server that
        // has not started yet. A list derived from traffic cannot see it.
        _log.Arm("not-yet-started", true);

        _log.ConnectionIds.Should().Contain("not-yet-started");
        (await CaptureQueries.ListAsync(_log, new EnvelopeFilter())).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearingOneConnectionLeavesTheOthers()
    {
        await AConversation("vb6");
        await AConversation("latex");

        await _log.ClearAsync("vb6");

        var all = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
        all.Entries.Should().OnlyContain(e => e.ConnectionId == "latex");
    }

    [Fact]
    public async Task ClearingEverythingTakesEveryConnection()
    {
        await AConversation("vb6");
        await AConversation("latex");

        var discarded = await _log.ClearAsync();

        discarded.Should().Be(4);
        (await CaptureQueries.ListAsync(_log, new EnvelopeFilter())).Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task AClearOnALogThatHasGoneAwayReturnsRatherThanHanging()
    {
        // It rides the same queue as the drain fence, so it inherits the same shutdown question.
        await using var log = new ConversationLog();
        await log.DisposeAsync();

        var cleared = await log.ClearAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cleared.Should().Be(0);
    }

    [Fact]
    public async Task AClearCannotRaceAFrameBeingRecorded()
    {
        // Ordered through the pump rather than applied in place: everything written before the call goes,
        // and the frames written after it survive. Done in place, this would tear whatever the pump was
        // holding at the time.
        _log.Arm("vb6", true);
        var client = Connect("vb6");

        for (var i = 0; i < 40; i++) await client.NotifyAsync("note", $"before-{i}");
        await _log.ClearAsync("vb6");
        for (var i = 0; i < 5; i++) await client.NotifyAsync("note", $"after-{i}");

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());

        page.Entries.Should().HaveCount(5);
        foreach (var entry in page.Entries)
        {
            var body = await CaptureQueries.FetchAsync(_log, "vb6", entry.Sequence);
            body!.Head.Should().Contain("after-", "nothing from before the clear survived it");
        }
    }
}
