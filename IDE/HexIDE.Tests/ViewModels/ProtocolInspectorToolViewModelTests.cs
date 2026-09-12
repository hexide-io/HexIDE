using System.Text;
using HexIDE.Conversations;
using HexIDE.Localization;
using HexIDE.Tools.ProtocolInspector;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The window over the capture.
/// </summary>
/// <remarks>
/// <b>These drive the view model against a real <see cref="ConversationLog"/>, not a substitute.</b> The
/// log is the thing whose behaviour the window has to respect — draining before reading, envelopes that
/// outlive their bodies, arming deciding what is kept — and a mock of it would let the window be wrong
/// about all three while the tests stayed green. It is in-process and cheap; there is no reason to fake it.
/// </remarks>
public class ProtocolInspectorToolViewModelTests : IAsyncDisposable
{
    private readonly ConversationLog _capture = new();

    public async ValueTask DisposeAsync()
    {
        await _capture.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static ILocalizationService Loc()
    {
        var loc = Substitute.For<ILocalizationService>();
        // Echo the key. An assertion can then tell the counters apart without depending on English, and a
        // missing format string shows up as the key rather than as an empty string.
        loc.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return loc;
    }

    private ProtocolInspectorToolViewModel Sut() => new(_capture, Loc());

    /// <summary>Records one frame the way the tap does, arming gate included.</summary>
    private void Frame(
        string connectionId, string method,
        ConversationDirection direction = ConversationDirection.Sent,
        ConversationEntryKind kind = ConversationEntryKind.Notification,
        string? detail = null)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\"}}");
        var keep = _capture.ShouldKeepBody(connectionId);

        _capture.Record(connectionId, direction, kind, method, null, body.Length,
            keep ? body : null, detail);
    }

    // ── Nothing to show ──────────────────────────────────────────────────────

    [Fact]
    public void AnEmptyRecordSaysWhyRatherThanShowingABlankGrid()
    {
        // The whole point of the window is to remove the ambiguity between "nothing happened", "nothing is
        // configured" and "this is broken". An empty grid reintroduces it, inside the thing built to
        // destroy it.
        var vm = Sut();

        vm.NothingToShow.Should().BeTrue();
        vm.EmptyReason.Should().Contain("first document",
            "a server starts lazily, so the commonest reason for an empty record is that nothing is open");
    }

    [Fact]
    public void AGridWithRowsCarriesNoExplanation()
    {
        Frame("vb6", "initialize");
        var vm = Sut();

        vm.NothingToShow.Should().BeFalse();
        vm.EmptyReason.Should().BeEmpty("an explanation on every ordinary view is noise, and noise is skipped");
    }

    // ── The timeline ─────────────────────────────────────────────────────────

    [Fact]
    public void EveryConnectionSharesOneTimeline()
    {
        // Not one server at a time. This IDE offers a document to every server that claims it, so "which
        // of you answered, and was the other even asked" is a native question here.
        Frame("vb6", "textDocument/didOpen");
        Frame("latex", "textDocument/didOpen");

        var vm = Sut();

        vm.Rows.Should().HaveCount(2);
        vm.Rows.Select(r => r.ConnectionId).Should().BeEquivalentTo(["vb6", "latex"]);
    }

    [Fact]
    public void TheServerFilterNarrowsToOne()
    {
        Frame("vb6", "a");
        Frame("latex", "b");

        var vm = Sut();
        vm.SelectedConnection = "vb6";

        vm.Rows.Should().OnlyContain(r => r.ConnectionId == "vb6");
        vm.ShownCount.Should().Be(1);
    }

    [Fact]
    public void TheConnectionListOffersEveryServerAndAll()
    {
        Frame("vb6", "a");
        Frame("latex", "b");

        var vm = Sut();

        vm.Connections.Should().Equal([ProtocolInspectorToolViewModel.AllConnections, "latex", "vb6"]);
        vm.SelectedConnection.Should().Be(ProtocolInspectorToolViewModel.AllConnections,
            "every server is the default, because the merged view is the one this IDE makes possible");
    }

    [Fact]
    public void AConnectionKnownButSilentIsStillOfferedInTheFilter()
    {
        // Arming names a connection before it has said anything. A filter built from traffic alone could
        // not offer it, and the reader would have no way to select the server they are waiting on.
        _capture.Arm("not-yet-talking", true);
        Frame("vb6", "a");

        Sut().Connections.Should().Contain("not-yet-talking");
    }

    // ── Failures ─────────────────────────────────────────────────────────────

    [Fact]
    public void AFailureIsMarkedInPlace()
    {
        Frame("vb6", "good");
        Frame("vb6", "bad", kind: ConversationEntryKind.ErrorResponse);

        var vm = Sut();

        vm.Rows.Should().HaveCount(2, "a failure stays in the timeline, because its meaning is in what "
                                    + "preceded it");
        vm.Rows.Count(r => r.IsFailure).Should().Be(1);
        vm.FailureCount.Should().Be(1);
        vm.HasFailures.Should().BeTrue();
    }

    [Fact]
    public void FailuresCanBeAskedForAlone()
    {
        Frame("vb6", "good");
        Frame("vb6", "bad", kind: ConversationEntryKind.ErrorResponse);

        var vm = Sut();
        vm.FailuresOnly = true;

        vm.Rows.Should().ContainSingle();
        vm.Rows[0].Method.Should().Be("bad");
    }

    [Fact]
    public void NoFailuresMeansTheCounterStaysOutOfTheWay()
    {
        Frame("vb6", "fine");

        Sut().HasFailures.Should().BeFalse();
    }

    // ── Rows ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ALocalNoteIsNotTraffic()
    {
        // Process lifecycle, standard error, a capability advertised and unused. They share the timeline
        // because chasing a failure across all three is the point, but they must not read as messages.
        Frame("vb6", "", direction: ConversationDirection.Local,
              kind: ConversationEntryKind.Lifecycle, detail: "connected");

        var row = Sut().Rows.Single();

        row.IsLocal.Should().BeTrue();
        row.Direction.Should().Be("•", "neither arrow, because it did not cross the wire");
        row.Method.Should().Be("connected", "the note takes the column a method would, or the row is blank");
    }

    [Fact]
    public void ADirectionIsAnArrow()
    {
        Frame("vb6", "out", direction: ConversationDirection.Sent);
        Frame("vb6", "in", direction: ConversationDirection.Received);

        Sut().Rows.Select(r => r.Direction).Should().Equal(["→", "←"]);
    }

    [Fact]
    public void ARequestStillOutstandingShowsNoLatencyRatherThanZero()
    {
        // A request that never came back is invisible in a record that only counts replies. A blank here
        // reads as a gap, and should.
        Frame("vb6", "hover", kind: ConversationEntryKind.Request);

        Sut().Rows.Single().Elapsed.Should().BeEmpty();
    }

    [Fact]
    public void ARetainedBodyIsFlaggedOnTheRow()
    {
        _capture.Arm("vb6", true);
        Frame("vb6", "didOpen");

        Sut().Rows.Single().HasBody.Should().BeTrue();
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheGridDoesNotMoveUntilItIsAskedTo()
    {
        // Comparing two rows is most of what this is for, and a grid that moved while being read would be
        // unusable for it. So new traffic is counted, not shown.
        Frame("vb6", "first");
        var vm = Sut();
        vm.ShownCount.Should().Be(1);

        Frame("vb6", "second");
        vm.NoteTraffic();

        vm.Rows.Should().ContainSingle("the grid is unchanged until refreshed");
        vm.HasPending.Should().BeTrue("but the reader is told there is more to get");
        vm.PendingCount.Should().Be(1);

        vm.Refresh();

        vm.Rows.Should().HaveCount(2);
        vm.HasPending.Should().BeFalse();
    }

    // ── Losses ───────────────────────────────────────────────────────────────

    [Fact]
    public void ARecordThatLostNothingSaysNothing()
    {
        Frame("vb6", "a");

        Sut().Losses.Should().BeEmpty("a banner on every clean capture would be ignored on the one that "
                                    + "is not clean");
    }

    [Fact]
    public async Task ARecordThatDiscardedSomethingSaysSo()
    {
        // A record that truncated silently reads exactly like a complete one. The ring is tiny here so the
        // eviction is certain rather than provoked by volume.
        await using var small = new ConversationLog(new CaptureLimits(EnvelopeEntries: 100, PrologueEntries: 0));
        for (var i = 0; i < 140; i++)
        {
            small.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification,
                $"n{i}", null, 10, null);
        }

        var vm = new ProtocolInspectorToolViewModel(small, Loc());

        vm.Losses.Should().NotBeEmpty();
        small.Losses("vb6").Envelopes.Should().BeGreaterThan(0, "the ring really did discard");
    }
}
