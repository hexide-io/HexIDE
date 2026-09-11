using System.Diagnostics;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.Conversations;

/// <summary>
/// That watching a conversation does not change it.
/// </summary>
/// <remarks>
/// <b>Every other test here asserts the capture is correct. These assert it is harmless</b>, which is a
/// different property and the one with teeth: a tap sits fully in-line on this pipeline, and twenty-five
/// milliseconds of work per call was measured turning ten round trips into six hundred.
///
/// <para>
/// The dangerous version of getting this wrong is not slowness, it is a capture that waits. A queue that
/// blocked when full would push the pump's backlog straight into the language service, so the IDE would
/// stall precisely when a server was busiest — which is when somebody is most likely to be watching.
/// </para>
/// </remarks>
public class TheTapStaysOutOfTheWayTests : IAsyncDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private ConversationLog? _log;

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables) { try { d.Dispose(); } catch { /* teardown */ } }
        if (_log is not null) await _log.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private sealed class Echo
    {
        [JsonRpcMethod("echo")]
        public int EchoBack(int n) => n;

        [JsonRpcMethod("note")]
        public void Note(int n) { }
    }

    private JsonRpc Connect(ConversationLog log)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var peer = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), new Echo());
        peer.StartListening();
        _disposables.Add(peer);

        var tapped = new CapturingFormatter(new SystemTextJsonFormatter(), log, "vb6");
        var client = new JsonRpc(new HeaderDelimitedMessageHandler(clientSide, clientSide, tapped));
        client.StartListening();
        _disposables.Add(client);
        return client;
    }

    [Fact]
    public async Task ASaturatedCaptureDropsRatherThanMakingTheProtocolWait()
    {
        // THE assertion. The queue is one deep and the traffic is heavy, so the pump is certainly behind —
        // and every call must still complete. A capture that waited would show up here as a test that never
        // finishes, which is exactly how it would show up to a user.
        //
        // Both halves matter. That the calls succeeded proves nothing blocked; that frames were dropped
        // proves the queue really was saturated, so the first half was not measured against an easy case.
        //
        // THE SECOND HALF USED TO BE A COIN TOSS, and that is worth recording rather than quietly fixing.
        // It awaited each round trip in turn, which hands the pump a scheduling opportunity between every
        // single frame — so on a fast machine a one-deep queue kept up and nothing was dropped, and the
        // test failed while nothing was wrong. Measured at roughly one run in three locally. Two changes
        // close it: fire a batch without awaiting between calls, so the writer genuinely outruns the
        // reader, and keep going until saturation is observed rather than assuming one batch produces it.
        _log = new ConversationLog(queueDepth: 1);
        var client = Connect(_log);
        _log.Arm("vb6", true);

        const int PerRound = 200;
        var sent = 0;

        for (var round = 0; round < 40 && _log.QueueDropped == 0; round++)
        {
            var calls = new List<Task<int>>(PerRound);
            for (var i = 0; i < PerRound; i++) calls.Add(client.InvokeAsync<int>("echo", sent + i));

            // Every one of them, not merely the batch: a capture that waited would stall an arbitrary
            // member of the batch, and a timeout here is the shape that failure takes.
            var answers = await Task.WhenAll(calls)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            answers.Should().Equal(Enumerable.Range(sent, PerRound),
                "the protocol must be unaffected by anything the capture is doing");
            sent += PerRound;
        }

        _log.QueueDropped.Should().BeGreaterThan(0,
            "a queue one deep under {0} concurrent round trips must have been saturated — if it was not, "
          + "this test proved nothing about a tap under pressure", sent);
    }

    [Fact]
    public async Task FramesAreRecordedInTheOrderTheyHappened()
    {
        // The pipeline guarantees frames arrive in order and a single-reader channel preserves it, so this
        // is not testing the channel — it is testing that nothing between the two reintroduces a race.
        // Sequence numbers are what a merged timeline is ordered by, so a shuffle here is a timeline that
        // silently lies about what followed what.
        _log = new ConversationLog();
        var client = Connect(_log);

        for (var i = 0; i < 200; i++) await client.NotifyAsync("note", i);
        await _log.DrainAsync();

        var notes = _log.Snapshot("vb6")
            .Where(e => e.Method == "note")
            .Select(e => e.Sequence)
            .ToArray();

        notes.Should().HaveCount(200);
        notes.Should().BeInAscendingOrder("sequence is what a merged timeline is ordered by");
    }

    [Fact]
    public async Task ARecordedRequestIsNeverSlowerThanTheServerAnsweringIt()
    {
        // A sanity bound rather than a benchmark, and generous by two orders of magnitude on purpose:
        // this fails on a tap that awaits, and does not fail on a slow machine. Anything tighter would be
        // a flake waiting for a busy build agent.
        _log = new ConversationLog();
        var client = Connect(_log);
        _log.Arm("vb6", true);

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++) await client.InvokeAsync<int>("echo", i);
        clock.Stop();

        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
            "two hundred in-memory round trips through a capture that copies bytes should be milliseconds; "
          + "twenty seconds means something in the tap is waiting rather than handing off");
    }

    [Fact]
    public async Task ATapThatThrowsDoesNotTakeTheConnectionWithIt()
    {
        // Measured on this library: a throw on the inbound path surfaces as a lost connection and the
        // connection stays lost, while the same throw outbound is survivable. So the capture swallows
        // unconditionally, and this is the assertion that it really does.
        //
        // Provoked by disposing the log out from under a live connection, which is the most plausible way
        // the capture can be made to fail in production — a session ending while a server is mid-sentence.
        _log = new ConversationLog();
        var client = Connect(_log);
        _log.Arm("vb6", true);

        await client.InvokeAsync<int>("echo", 1);
        await _log.DisposeAsync();

        var after = await client.InvokeAsync<int>("echo", 2).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        after.Should().Be(2, "a capture that has gone away must not take the language service with it");
    }

    [Fact]
    public async Task TheDroppedCountIsTheTruth()
    {
        // The counter exists because a record that quietly loses frames reads exactly like one that had
        // fewer to lose. It could not work at all with the obvious channel setting, which discards the item
        // and reports success — so this asserts the count moves rather than trusting that it would.
        _log = new ConversationLog(queueDepth: 1);
        var client = Connect(_log);

        for (var i = 0; i < 1000; i++) await client.NotifyAsync("note", i);
        await _log.DrainAsync();

        var recorded = _log.Snapshot("vb6").Count;
        var dropped = _log.QueueDropped;

        dropped.Should().BeGreaterThan(0);

        // EXACTLY a thousand, not at least. The weaker form was what let the drain's own gap hide: with
        // the fence silently skipped when the queue was full, this read the record one frame early and
        // failed by one — which looks like an accounting hole in the capture and was a hole in the drain.
        // An inequality here would have passed either way.
        (recorded + dropped).Should().Be(1000,
            "everything that happened is either in the record or in the count of what is missing from it — "
          + "a frame that is in neither has been lost silently, which is the one outcome this design "
          + "refuses everywhere");
    }

    [Fact]
    public async Task ADrainOnALogThatHasGoneAwayReturnsRatherThanHanging()
    {
        // The drain waits for room in the queue, so the one thing that could leave a caller stuck forever
        // is a queue nobody is reading any more. Export calls this, and an export during shutdown is not a
        // strange thing to happen.
        _log = new ConversationLog(queueDepth: 1);
        var client = Connect(_log);

        for (var i = 0; i < 100; i++) await client.NotifyAsync("note", i);
        await _log.DisposeAsync();

        await _log.DrainAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
