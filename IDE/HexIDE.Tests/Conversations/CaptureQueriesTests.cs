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

    // Holds "slow" unanswered until a test lets it go, so a request can be seen waiting and then answered.
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables) { try { d.Dispose(); } catch { /* teardown */ } }
        await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private sealed class Server(Task release)
    {
        [JsonRpcMethod("slow")]
        public async Task<int> Slow() { await release; return 1; }

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
            new Server(_release.Task));
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
    public async Task A_message_this_client_could_not_serialize_is_a_failure_and_a_withheld_request_is_not()
    {
        // #945: failuresOnly hid the one client-side defect the capture was built to expose.
        _log.Record("vb6", ConversationDirection.Local, ConversationEntryKind.NeverSent, "$/cancelRequest", null, 0,
            detail: "could not be serialized: no JsonTypeInfo");
        _log.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.NeverSent, "textDocument/hover", null, 0,
            detail: "hoverProvider");

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(FailuresOnly: true));

        page.Entries.Should().ContainSingle().Which.Method.Should().Be("$/cancelRequest");
    }

    [Fact]
    public async Task PollingForWhatIsNewReturnsOnlyThat()
    {
        var client = await AConversation();
        var soFar = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
        var latest = soFar.LatestSequence;

        await client.InvokeWithParameterObjectAsync<JsonElement>(
            "textDocument/hover", new { line = 2 }, TestContext.Current.CancellationToken);

        var since = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: latest));

        since.Entries.Should().ContainSingle(
            "exercising one thing and asking what is new is the cheap loop this exists for");
    }

    /// <summary>Sends "slow" and waits until the record holds it, unanswered.</summary>
    private async Task<(Task<int> Reply, EnvelopePage Seen)> ARequestLeftWaiting(JsonRpc client)
    {
        var reply = client.InvokeAsync<int>("slow");
        for (var i = 0; i < 250; i++)
        {
            var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
            if (page.Entries.Any(e => e.Method == "slow")) return (reply, page);
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("slow was never recorded");
    }

    [Fact]
    public async Task A_request_seen_waiting_comes_back_answered_to_the_next_poll()
    {
        // #961: a poller passing the highest sequence it had seen never saw a waiting request answered, because the
        // reply completes the request's row, which is below that sequence.
        var client = await AConversation();
        var (reply, seen) = await ARequestLeftWaiting(client);
        seen.Entries.Single(e => e.Method == "slow").Outcome.Should().Be(ConversationOutcome.None);

        await client.NotifyAsync("note", "after it");
        var meanwhile = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: seen.LatestSequence));
        meanwhile.Entries.Select(e => e.Method).Should().Equal(["note"], "slow is still waiting, so it has not changed");

        _release.SetResult();
        await reply;
        var answered = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: meanwhile.LatestSequence));

        answered.Entries.Should().ContainSingle().Which.Should().Match<ConversationEnvelope>(e =>
            e.Method == "slow" && e.Outcome == ConversationOutcome.Answered);
        answered.LatestSequence.Should().BeGreaterThan(answered.Entries[0].Sequence,
            "the row is older than the reply it now carries, so its own sequence is not where to poll from");
        (await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: answered.LatestSequence)))
            .Entries.Should().BeEmpty("polling from latestSequence returns an answer once, not on every poll");
    }

    [Fact]
    public async Task A_request_ended_unanswered_comes_back_ended_to_the_next_poll()
    {
        var client = await AConversation();
        var (reply, seen) = await ARequestLeftWaiting(client);
        try
        {
            _log.EndRequests("vb6", ConversationDirection.Sent, outcome: ConversationOutcome.Cancelled);

            var ended = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: seen.LatestSequence));

            ended.Entries.Should().ContainSingle().Which.Outcome.Should().Be(ConversationOutcome.Cancelled);
            (await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: ended.LatestSequence)))
                .Entries.Should().BeEmpty();
        }
        finally
        {
            _release.SetResult();
            await reply;
        }
    }

    [Fact]
    public async Task A_poll_cut_short_by_its_limit_pages_on_without_losing_any()
    {
        // #961 review: the cursor was the newest change looked at, so a poll that kept the newest of more than its
        // limit skipped the rest for good. A poll keeps the earliest changes and its cursor continues from them.
        var client = await AConversation();
        var (reply, seen) = await ARequestLeftWaiting(client);
        await client.NotifyAsync("note", "one");
        await client.NotifyAsync("note", "two");
        _release.SetResult();
        await reply;

        var pages = new List<EnvelopePage>();
        var cursor = seen.LatestSequence;
        for (var i = 0; i < 4; i++)
        {
            var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: cursor, Limit: 1));
            pages.Add(page);
            cursor = page.LatestSequence;
        }

        pages.Select(p => p.Entries.Select(e => e.Method).SingleOrDefault()).Should().Equal(
            ["note", "note", "slow", null], "the notes were recorded before slow was answered");
        pages.Select(p => p.Truncated).Should().Equal([true, true, false, false]);
        pages[2].Entries[0].Outcome.Should().Be(ConversationOutcome.Answered);
    }

    [Fact]
    public async Task Polling_from_latestSequence_across_connections_misses_nothing()
    {
        // #961 review: the snapshot reads one ring at a time while the pump carries on, so a cursor taken from what was
        // read skipped what an earlier ring took meanwhile; measured, hundreds of rows in two hundred thousand.
        var writer = Task.Run(() =>
        {
            for (var i = 0; i < 150_000; i++)
                _log.Record((i % 3) switch { 0 => "a", 1 => "b", _ => "c" }, ConversationDirection.Sent, ConversationEntryKind.Notification,
                    "note", null, 1);
        }, TestContext.Current.CancellationToken);

        var polled = new HashSet<long>();
        long? cursor = 0;
        while (true)
        {
            var finished = writer.IsCompleted;
            var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: cursor, Limit: int.MaxValue));
            polled.UnionWith(page.Entries.Select(e => e.Sequence));
            cursor = page.LatestSequence;
            if (finished) break;
        }

        var all = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Limit: int.MaxValue));
        all.Entries.Select(e => e.Sequence).Except(polled).Should().BeEmpty();
    }

    [Fact]
    public async Task Requests_ended_together_come_back_together_to_a_limited_poll()
    {
        // One ending takes one sequence for every request it ends. A limit that split them would leave the rest
        // behind a cursor already past their sequence.
        var client = await AConversation();
        var (first, _) = await ARequestLeftWaiting(client);
        var second = client.InvokeAsync<int>("slow");
        var seen = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
        for (var i = 0; i < 250 && seen.Entries.Count(e => e.Method == "slow") < 2; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
            seen = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
        }

        try
        {
            _log.EndRequests("vb6", ConversationDirection.Sent, outcome: ConversationOutcome.Abandoned);

            var ended = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: seen.LatestSequence, Limit: 1));

            ended.Entries.Should().HaveCount(2).And.OnlyContain(e => e.Outcome == ConversationOutcome.Abandoned);
            ended.Entries.Select(e => e.CompletedSequence).Distinct().Should().ContainSingle();
            ended.LatestSequence.Should().Be(ended.Entries[0].CompletedSequence);
        }
        finally
        {
            _release.SetResult();
            await Task.WhenAll(first, second);
        }
    }

    [Fact]
    public async Task A_cursor_from_an_earlier_run_lists_this_run_from_its_start_and_says_why()
    {
        await AConversation();
        var all = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());

        var page = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(AfterSequence: 50_000));

        page.Entries.Should().Equal(all.Entries);
        page.LatestSequence.Should().Be(all.LatestSequence, "a cursor beyond the record is not handed back");
        page.Note.Should().StartWith($"afterSequence 50000 is beyond the last sequence recorded, {all.LatestSequence}: "
                                     + "sequences start again when the IDE does");
    }

    [Fact]
    public async Task The_sequence_a_request_was_answered_or_ended_under_is_explained_not_called_missing()
    {
        // Recorded without bodies, as an unarmed connection past its opening records them.
        _log.Record("raw", ConversationDirection.Sent, ConversationEntryKind.Request, "textDocument/hover", "7", 90);
        _log.Record("raw", ConversationDirection.Received, ConversationEntryKind.Response, null, "7", 40);
        _log.Record("raw", ConversationDirection.Sent, ConversationEntryKind.Request, "slow", "8", 30);
        _log.EndRequests("raw", ConversationDirection.Sent, outcome: ConversationOutcome.Cancelled);
        var rows = (await CaptureQueries.ListAsync(_log, new EnvelopeFilter(ConnectionId: "raw"))).Entries;
        var (hover, slow) = (rows.Single(e => e.Method == "textDocument/hover"), rows.Single(e => e.Method == "slow"));

        hover.AnswerSequence.Should().BeNull("no body was kept for the answer");
        (await CaptureQueries.ExplainMissingBodyAsync(_log, "raw", hover.CompletedSequence!.Value)).Should().Be(
            $"Sequence {hover.CompletedSequence} is where request {hover.Sequence} (textDocument/hover) was answered, and "
            + "no body was kept for the answer, so there is nothing to fetch; its size is on the request's row.");
        (await CaptureQueries.ExplainMissingBodyAsync(_log, "raw", slow.CompletedSequence!.Value)).Should().Be(
            $"Sequence {slow.CompletedSequence} is where request {slow.Sequence} (slow) was cancelled. An ending has no body.");
    }

    [Fact]
    public async Task A_listing_names_the_last_sequence_it_looked_at_even_when_nothing_matched()
    {
        var client = await AConversation();
        var all = await CaptureQueries.ListAsync(_log, new EnvelopeFilter());
        var none = await CaptureQueries.ListAsync(_log, new EnvelopeFilter(Method: "no/such"));

        none.LatestSequence.Should().Be(all.LatestSequence)
            .And.BeGreaterThan(all.Entries[^1].Sequence, "the hover's reply used up the last sequence");
        (await CaptureQueries.ListAsync(new ConversationLog(), new EnvelopeFilter())).LatestSequence.Should().BeNull();
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
            new Server(_release.Task));
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
