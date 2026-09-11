using System.Text;
using HexIDE.Conversations;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// The bounded record a conversation is kept in.
///
/// <para>
/// Almost everything here is about what happens when it is <b>full</b>, because that is the only state in
/// which the design decisions are visible. An empty ring behaves the same however it was built.
/// </para>
/// </summary>
public class ConversationRingTests
{
    private static CaptureLimits Small(int entries, int prologue) =>
        CaptureLimits.Default with { EnvelopeEntries = entries, PrologueEntries = prologue };

    private static ConversationEnvelope At(long sequence) => new(
        sequence, "vb6", DateTimeOffset.UnixEpoch, ConversationDirection.Sent,
        ConversationEntryKind.Notification, "textDocument/didChange", null, 100);

    [Fact]
    public void TheHandshakeSurvivesAFullSession()
    {
        // THE assertion of this class. Plain drop-oldest evicts `initialize` first, which is exactly the
        // thing that must never go: several of this project's costliest defects live in or immediately
        // after it, and one of them turned on the CONTENT of the initialize reply.
        var ring = new ConversationRing(Small(entries: 10, prologue: 3));

        for (var i = 0; i < 500; i++) ring.Add(At(i));

        var held = ring.Snapshot();
        held.Take(3).Select(e => e.Sequence).Should().Equal([0L, 1L, 2L],
            "the prologue is pinned, so the opening of the conversation outlives the middle of it");
        held.Should().HaveCount(10, "the prologue counts against the limit rather than sitting on top of it");
        held[^1].Sequence.Should().Be(499, "the most recent is always kept");
    }

    [Fact]
    public void EveryDiscardIsCounted()
    {
        // A record that truncates silently reads exactly like a complete one. That is the same shape as a
        // guard that skips instead of failing, which this project has already paid for once.
        var ring = new ConversationRing(Small(entries: 10, prologue: 0));

        for (var i = 0; i < 25; i++) ring.Add(At(i));

        ring.Dropped.Should().Be(15);
        ring.Count.Should().Be(10);
    }

    [Fact]
    public void ARequestIsCompletedInPlaceRatherThanRecordedTwice()
    {
        // A request is written when it is SENT, because one that never comes back has to appear — that
        // absence is a finding. Its outcome is only knowable later, and appending a second entry would
        // double every request in a timeline whose entire job is to be read in order.
        var ring = new ConversationRing(Small(entries: 10, prologue: 0));
        ring.Add(At(1) with { Kind = ConversationEntryKind.Request, CorrelationId = "7" });

        ring.Complete(1, ConversationOutcome.Answered, TimeSpan.FromMilliseconds(41));

        ring.Snapshot().Should().ContainSingle();
        ring.Snapshot()[0].Outcome.Should().Be(ConversationOutcome.Answered);
        ring.Snapshot()[0].Elapsed.Should().Be(TimeSpan.FromMilliseconds(41));
    }

    [Fact]
    public void CompletingSomethingAlreadyDroppedIsNotAnError()
    {
        // A long-running request whose envelope has aged out of a busy ring is the ordinary case, not a
        // bug. There is nothing left to complete and nothing to complain about.
        var ring = new ConversationRing(Small(entries: 5, prologue: 0));
        ring.Add(At(1) with { Kind = ConversationEntryKind.Request });
        for (var i = 10; i < 30; i++) ring.Add(At(i));

        var complete = () => ring.Complete(1, ConversationOutcome.Answered, TimeSpan.Zero);

        complete.Should().NotThrow();
        ring.Count.Should().Be(5);
    }

    [Fact]
    public void APinnedRequestIsStillCompletable()
    {
        // The prologue is where the handshake lives, so `initialize` is a request that sits there for the
        // life of the connection. If completion only searched the rotating part, the one request most
        // worth having a duration for would never get one.
        var ring = new ConversationRing(Small(entries: 10, prologue: 3));
        ring.Add(At(0) with { Kind = ConversationEntryKind.Request, Method = "initialize" });
        for (var i = 1; i < 40; i++) ring.Add(At(i));

        ring.Complete(0, ConversationOutcome.Answered, TimeSpan.FromSeconds(1));

        ring.Snapshot()[0].Elapsed.Should().Be(TimeSpan.FromSeconds(1));
    }
}

/// <summary>Limits arriving from a file somebody edits by hand.</summary>
public class CaptureLimitsTests
{
    [Fact]
    public void ANonsenseValueIsClampedAndSaidOutLoud()
    {
        // Clamped rather than rejected: a zero or an extra three digits are ordinary typing accidents, and
        // refusing the whole configuration over one would cost a working capture. Said out loud, because a
        // limit that silently became a different limit is a limit nobody set.
        var limits = (CaptureLimits.Default with { EnvelopeEntries = 0 }).Clamped(out var adjustments);

        limits.EnvelopeEntries.Should().Be(100);
        adjustments.Should().Contain(note => note.Contains("envelope entries"));

        // And the correction CASCADES, which is the part worth pinning. The default prologue is 200, so
        // once the ring is clamped to 100 the prologue no longer fits inside it and is corrected too.
        // Bounding each value against a constant instead would leave a configuration that passed
        // validation and could never evict anything.
        limits.PrologueEntries.Should().Be(50);
        adjustments.Should().Contain(note => note.Contains("prologue entries"));
    }

    [Fact]
    public void APrologueCannotSwallowTheWholeRing()
    {
        // A prologue as long as the ring would evict nothing and grow without bound, which turns a bounded
        // record into a leak by way of a configuration file.
        var limits = (CaptureLimits.Default with { EnvelopeEntries = 1000, PrologueEntries = 999 })
            .Clamped(out _);

        limits.PrologueEntries.Should().Be(500);
    }

    [Fact]
    public void AConnectionCannotBeGivenMoreThanTheCeiling()
    {
        // Bounded against its neighbour rather than a constant: a per-connection budget above the global
        // ceiling does not raise the ceiling, it just makes the ceiling a lie.
        var limits = (CaptureLimits.Default with
        {
            GlobalPayloadBytes = 8L * 1024 * 1024,
            PayloadBytesPerConnection = 64L * 1024 * 1024,
        }).Clamped(out var adjustments);

        limits.PayloadBytesPerConnection.Should().Be(8L * 1024 * 1024);
        adjustments.Should().Contain(note => note.Contains("payload bytes per connection"));
    }

    [Fact]
    public void TheDefaultsAreAlreadyValid()
    {
        CaptureLimits.Default.Clamped(out var adjustments);
        adjustments.Should().BeEmpty("shipping a default the clamp has to correct would be embarrassing");
    }
}

/// <summary>The bodies, which are the expensive half and the only half that discloses anything.</summary>
public class PayloadStoreTests
{
    private static PayloadStore Store(CaptureLimits? limits = null, PayloadBudget? budget = null)
    {
        var effective = limits ?? CaptureLimits.Default;
        return new PayloadStore(effective, budget ?? new PayloadBudget(effective.GlobalPayloadBytes));
    }

    private static byte[] Body(int length, char fill = 'x') =>
        Encoding.UTF8.GetBytes(new string(fill, length));

    [Fact]
    public void ASmallBodyIsKeptWhole()
    {
        var store = Store();
        store.TryAdd(1, Body(100)).Should().BeTrue();

        var kept = store.Find(1);
        kept!.IsTruncated.Should().BeFalse();
        kept.TrueLength.Should().Be(100);
    }

    [Fact]
    public void ALargeBodyKeepsBothEndsAndSaysHowLongItReallyWas()
    {
        // Both ends rather than a prefix: JSON cut off at the front alone is unreadable, and in a
        // full-document sync frame the recent edit is usually near the tail. The true length is stated
        // because a reader who does not know a frame is shortened will draw conclusions from a shape that
        // was never on the wire.
        var store = Store(CaptureLimits.Default with { FrameBytes = 1024 });

        store.TryAdd(1, Body(10_000)).Should().BeTrue();

        var kept = store.Find(1)!;
        kept.IsTruncated.Should().BeTrue();
        kept.TrueLength.Should().Be(10_000);
        kept.RetainedBytes.Should().BeLessThanOrEqualTo(1024);
        kept.Tail.Should().NotBeNull();
    }

    [Fact]
    public void IdenticalBodiesArePaidForOnce()
    {
        // The highest-yield thing this class does. Full-document sync re-sends the whole file on every
        // change, and the client flushes again on an opening parenthesis, on a comma, and on save — so the
        // same bytes cross the wire several times a second.
        var store = Store();
        var body = Body(4096);

        store.TryAdd(1, body);
        var afterOne = store.RetainedBytes;
        for (var i = 2; i <= 50; i++) store.TryAdd(i, body);

        store.RetainedBytes.Should().Be(afterOne, "fifty copies of one body cost what one body costs");
        store.Find(50).Should().NotBeNull();
    }

    [Fact]
    public void ADeduplicatedBodySurvivesUntilItsLastReferenceGoes()
    {
        // Refcounting rather than eviction-by-sequence. Getting this wrong would free bytes that a newer
        // entry still points at, and the newer entry would then read as though it had been dropped.
        var store = Store(CaptureLimits.Default with { PayloadBytesPerConnection = 8192, FrameBytes = 4096 });
        var shared = Body(2048, 'a');

        store.TryAdd(1, shared);
        store.TryAdd(2, shared);
        store.TryAdd(3, Body(2048, 'b'));
        store.TryAdd(4, Body(2048, 'c'));
        store.TryAdd(5, Body(2048, 'd'));   // forces eviction of sequence 1

        store.Find(2).Should().NotBeNull(
            "sequence 2 points at the same bytes as the evicted sequence 1, and they are still needed");
    }

    [Fact]
    public void EvictionIsCountedAndSeparateFromRefusal()
    {
        // Two different things happen when there is no room, and a reader needs to tell them apart. A drop
        // is this connection's own history rotating. A refusal is it being unable to record anything new,
        // which at the global ceiling means somebody else's traffic is crowding it out.
        var store = Store(CaptureLimits.Default with { PayloadBytesPerConnection = 4096, FrameBytes = 4096 });

        for (var i = 1; i <= 10; i++) store.TryAdd(i, Body(1024, (char)('a' + i)));

        store.DroppedBodies.Should().BeGreaterThan(0);
        store.RefusedBodies.Should().Be(0, "there was always something of its own to evict");
    }

    [Fact]
    public void ABodyLargerThanEverythingIsRefusedRatherThanThrowing()
    {
        // A capture that cannot hold one enormous frame must carry on holding the rest. Measured: a single
        // completion reply from one fixture server was nearly 37 KB, and a full-document change on a large
        // module runs to hundreds.
        var limits = CaptureLimits.Default with
        {
            PayloadBytesPerConnection = 2048,
            GlobalPayloadBytes = 2048,
            FrameBytes = 1_000_000,
        };
        var store = Store(limits, new PayloadBudget(limits.GlobalPayloadBytes));

        store.TryAdd(1, Body(500_000)).Should().BeFalse();
        store.RefusedBodies.Should().Be(1);
    }

    [Fact]
    public void OneConnectionCannotSpendAnotherConnectionsBudget()
    {
        // The reason budgets are per connection. Measured on an identical editing script, one server
        // returned roughly a hundred times another's inbound bytes — so under a single shared pool the
        // noisy one silently evicts the quiet one's entire history.
        var limits = CaptureLimits.Default with
        {
            PayloadBytesPerConnection = 4096,
            GlobalPayloadBytes = 1024 * 1024,
            FrameBytes = 4096,
        };
        var shared = new PayloadBudget(limits.GlobalPayloadBytes);
        var quiet = new PayloadStore(limits, shared);
        var noisy = new PayloadStore(limits, shared);

        quiet.TryAdd(1, Body(1024, 'q'));
        for (var i = 100; i < 200; i++) noisy.TryAdd(i, Body(1024, (char)('a' + (i % 26))));

        quiet.Find(1).Should().NotBeNull("the noisy connection spends its own budget, not the quiet one's");
        quiet.DroppedBodies.Should().Be(0);
    }

    [Fact]
    public void ClearingGivesTheBudgetBack()
    {
        var budget = new PayloadBudget(1024 * 1024);
        var store = new PayloadStore(CaptureLimits.Default, budget);
        store.TryAdd(1, Body(4096));
        budget.Used.Should().BeGreaterThan(0);

        store.Clear();

        budget.Used.Should().Be(0);
        store.RetainedBytes.Should().Be(0);
    }
}
