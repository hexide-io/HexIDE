using Avalonia.Platform.Storage;
using System.IO;
using HexIDE.Redaction;
using HexIDE.IDE;
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
        //
        // The two exceptions are the format strings the detail pane composes with. Echoing those would
        // swallow their arguments, and the arguments — a byte count, a sequence number — are the whole
        // content of what the assertion is checking.
        loc.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>() switch
        {
            "Str.Tool.ProtocolInspector.Truncated" => "-- {0} of {1} bytes not kept --",
            "Str.Tool.ProtocolInspector.DetailHeader" => "#{0} {1} {2}",
            "Str.Tool.ProtocolInspector.Exported" => "Exported {0} messages to {1} and {2}",
            var key => key,
        });
        return loc;
    }

    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();

    /// <summary>Answers the export preview with Save, for the tests that are about what comes after it.</summary>
    private void PreviewAccepted() => _windows.ShowDialog(Arg.Any<IDialog>()).Returns(true);

    private ProtocolInspectorToolViewModel Sut() => Over(_capture);

    private ProtocolInspectorToolViewModel Over(ConversationLog log) =>
        new(log, Loc(), new Pseudonymiser(), _windows);

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

        var vm = Over(small);

        vm.Losses.Should().NotBeEmpty();
        small.Losses("vb6").Envelopes.Should().BeGreaterThan(0, "the ring really did discard");
    }

    // ── The body ─────────────────────────────────────────────────────────────

    [Fact]
    public void NothingIsSelectedUntilSomethingIs()
    {
        Frame("vb6", "initialize");

        var vm = Sut();

        vm.IsDetailOpen.Should().BeFalse("a pane that opened on its own would take space from the grid "
                                       + "before anyone had asked to read a row");
    }

    [Fact]
    public void SelectingARowShowsWhatCrossedTheWire()
    {
        _capture.Arm("vb6", true);
        Frame("vb6", "textDocument/didOpen");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.IsDetailOpen.Should().BeTrue();
        vm.HasSelectedBody.Should().BeTrue();
        vm.SelectedBody.Should().Contain("textDocument/didOpen");
        vm.SelectedBodyUnavailable.Should().BeEmpty();
    }

    [Fact]
    public void DeselectingClosesThePaneRatherThanLeavingTheLastBodyOnScreen()
    {
        // A stale body under a grid the reader has moved on from is worse than no body: it reads as the
        // current row's content and nothing on screen says otherwise.
        _capture.Arm("vb6", true);
        Frame("vb6", "initialize");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();
        vm.SelectedRow = null;

        vm.IsDetailOpen.Should().BeFalse();
        vm.SelectedBody.Should().BeEmpty();
        vm.SelectedBodyHeader.Should().BeEmpty();
    }

    [Fact]
    public void TheHeaderNamesTheRowThePaneIsShowing()
    {
        // Two rows can carry the same method a second apart, and the pane is below a grid that scrolls. The
        // sequence is what ties what is on screen to the row that was clicked.
        _capture.Arm("vb6", true);
        Frame("vb6", "textDocument/hover");

        var vm = Sut();
        var row = vm.Rows.Single();
        vm.SelectedRow = row;

        vm.SelectedBodyHeader.Should().Be($"#{row.Sequence} vb6 textDocument/hover");
    }

    [Fact]
    public void AnUnarmedConnectionSaysSoRatherThanShowingABlankPane()
    {
        // The commonest reason a body is missing, and the only one the reader can do something about. A
        // blank pane here reads as a defect in the inspector.
        //
        // Past the opening allowance deliberately. A connection's first few frames are kept whether or not
        // it is armed — that is the handshake rule — so a one-frame capture would show a body and prove
        // nothing about the unarmed case.
        for (var i = 0; i <= ConversationLog.OpeningFrames; i++) Frame("vb6", $"textDocument/didChange{i}");

        var vm = Sut();
        var last = vm.Rows.Last();
        last.HasBody.Should().BeFalse("this frame is past the opening allowance on an unarmed connection");

        vm.SelectedRow = last;

        vm.HasSelectedBody.Should().BeFalse();
        vm.SelectedBodyUnavailable.Should().Contain("not armed");
    }

    [Fact]
    public void ALocalNoteSaysItNeverHadABodyAtAll()
    {
        // Distinct from "nothing was retained", which would imply something could have been. A lifecycle
        // note is the capture talking about the process, not a message that was shortened away.
        // Recorded the way the tap records one: no bytes, because there were none. Going through the
        // arming gate would offer a body this entry never has.
        _capture.Record("vb6", ConversationDirection.Local, ConversationEntryKind.Lifecycle,
            null, null, 0, null, "process exited with 1");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.HasSelectedBody.Should().BeFalse();
        vm.SelectedBodyUnavailable.Should().Be("Str.Tool.ProtocolInspector.NoteHasNoBody");
    }

    [Fact]
    public async Task ATruncatedBodyStatesTheGapRatherThanClosingIt()
    {
        // Head joined straight to tail would parse as JSON and lie about what was sent — the one outcome
        // this whole capture refuses. The gap is marked, and measured in the bytes that crossed the wire.
        //
        // The frame cap is set to its floor rather than provoked with a megabyte: the behaviour under test
        // is what the pane does with a shortened body, not how large a body has to be to get shortened.
        await using var small = new ConversationLog(new CaptureLimits(FrameBytes: 1024));
        small.Arm("vb6", true);

        var body = Encoding.UTF8.GetBytes(
            $"{{\"jsonrpc\":\"2.0\",\"result\":\"{new string('x', 4000)}\",\"end\":true}}");
        small.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            null, "1", body.Length, body);

        var vm = Over(small);
        vm.SelectedRow = vm.Rows.Single();

        vm.HasSelectedBody.Should().BeTrue();

        var stored = small.Body("vb6", vm.Rows.Single().Sequence);
        stored.Should().NotBeNull();
        stored!.Tail.Should().NotBeNull("the frame cap really did shorten this body");

        var missing = stored.TrueLength - stored.Head.Length - stored.Tail!.Length;
        vm.SelectedBody.Should().Contain($"-- {missing} of {body.Length} bytes not kept --");

        // And the two halves are not joined. What is on screen must not read as a complete frame.
        vm.SelectedBody.Should().NotContain(new string('x', 4000));
    }

    // ── What leaves the machine ──────────────────────────────────────────────

    [Fact]
    public void NothingSelectedMeansNothingToCopy()
    {
        Frame("vb6", "initialize");

        Sut().CanCopy.Should().BeFalse();
    }

    [Fact]
    public void TheCopyIsTheTraceShapeAServerAuthorRecognises()
    {
        _capture.Arm("vb6", true);
        Frame("vb6", "textDocument/didOpen");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.CanCopy.Should().BeTrue();
        vm.SelectedTrace.Should().Contain("[Trace - ")
                        .And.Contain("[vb6]")
                        .And.Contain("Sending notification 'textDocument/didOpen'.");
    }

    [Fact]
    public void AMessageWithNoBodyIsStillCopyable()
    {
        // Often exactly the finding — this was sent and never answered — so refusing to copy it would
        // withhold the most quotable row there is.
        _capture.Record("vb6", ConversationDirection.Local, ConversationEntryKind.Lifecycle,
            null, null, 0, null, "process exited with 1");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.CanCopy.Should().BeTrue();
        vm.SelectedTrace.Should().Contain("process exited with 1");
    }

    [Fact]
    public void WhatIsCopiedIsRedactedEvenThoughWhatIsShownIsNot()
    {
        // The rule the whole design turns on. The pane is the developer looking at their own machine; the
        // copy is addressed to somebody else, so a path in it is replaced by a stable made-up name.
        _capture.Arm("vb6", true);

        var body = System.Text.Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":"""
            + """{"textDocument":{"uri":"file:///C:/Repos/Secret/Ledger.frm"}}}""");
        _capture.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification,
            "textDocument/didOpen", null, body.Length, body);

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.SelectedBody.Should().Contain("Secret", "the live pane is the bytes that crossed the wire");
        vm.SelectedTrace.Should().NotContain("Secret");
        vm.SelectedTrace.Should().NotContain("Ledger");
    }

    [Fact]
    public async Task ACancelledExportWritesNothingAndSaysNothing()
    {
        PreviewAccepted();
        // A picker returning null is the reader changing their mind. Reporting a failure for it would
        // teach them to ignore the line that also reports real ones.
        Frame("vb6", "initialize");
        _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns((string?)null);

        var vm = Sut();
        await vm.ExportAsync();

        vm.ExportStatus.Should().BeEmpty();
    }

    [Fact]
    public async Task AnExportWritesBothHalvesUnderOneStemAndSaysWhere()
    {
        PreviewAccepted();
        // Two files, and the manifest's name is derived rather than asked for a second time: the manifest
        // cites the messages file by line number, so a separated pair is a broken one.
        _capture.Arm("vb6", true);
        Frame("vb6", "initialize");

        var folder = Directory.CreateTempSubdirectory("hexide-export-test");
        try
        {
            var chosen = Path.Combine(folder.FullName, "conversation.jsonl");
            _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(chosen);

            var vm = Sut();
            await vm.ExportAsync();

            var manifest = Path.Combine(folder.FullName, "conversation.manifest.json");
            File.Exists(chosen).Should().BeTrue();
            File.Exists(manifest).Should().BeTrue();

            File.ReadAllText(chosen).Should().Contain("initialize");
            vm.ExportStatus.Should().Contain(chosen).And.Contain(manifest);
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public async Task AnExportThatFailsSaysSoRatherThanLookingLikeACancel()
    {
        PreviewAccepted();
        // Silence after a Save button is indistinguishable from a cancel, and the reader will act on the
        // wrong one — most likely by concluding the record was empty.
        Frame("vb6", "initialize");
        _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>())
                .Returns(Path.Combine(Path.GetTempPath(), "no-such-directory-here", "x.jsonl"));

        var vm = Sut();
        await vm.ExportAsync();

        vm.ExportStatus.Should().NotBeEmpty();
        vm.ExportStatus.Should().Contain("Str.Tool.ProtocolInspector.ExportFailed",
            "the failure is reported through the localised key, not swallowed");
    }

    [Fact]
    public async Task ACancelAfterASuccessDoesNotLeaveTheOldSuccessOnScreen()
    {
        PreviewAccepted();
        // Found against the running window. The line is true — that export did happen — and it reads as
        // though the one just cancelled had happened too, which is the reading a reader will take.
        _capture.Arm("vb6", true);
        Frame("vb6", "initialize");

        var folder = Directory.CreateTempSubdirectory("hexide-export-stale");
        try
        {
            var chosen = Path.Combine(folder.FullName, "first.jsonl");
            _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(chosen);

            var vm = Sut();
            await vm.ExportAsync();
            vm.ExportStatus.Should().NotBeEmpty();

            _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns((string?)null);
            await vm.ExportAsync();

            vm.ExportStatus.Should().BeEmpty();
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public async Task TheServerFilterDecidesWhatIsExported()
    {
        PreviewAccepted();
        // The window shows one server at a time when asked to, and an export that ignored that would hand
        // somebody a file full of another server's traffic.
        _capture.Arm("vb6", true);
        _capture.Arm("latex", true);
        Frame("vb6", "initialize");
        Frame("latex", "initialize");

        var folder = Directory.CreateTempSubdirectory("hexide-export-filter");
        try
        {
            var chosen = Path.Combine(folder.FullName, "one.jsonl");
            _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(chosen);

            var vm = Sut();
            vm.SelectedConnection = "latex";
            await vm.ExportAsync();

            File.ReadAllText(Path.Combine(folder.FullName, "one.manifest.json"))
                .Should().Contain("latex").And.NotContain("\"vb6\"");
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public async Task NothingLeavesUntilThePreviewIsAccepted()
    {
        // The preview is the only thing that catches a secret sitting in a string literal, so declining it
        // has to mean nothing was written and nothing was even asked for.
        _capture.Arm("vb6", true);
        Frame("vb6", "initialize");

        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(false);

        var vm = Sut();
        await vm.ExportAsync();

        await _windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>());
        vm.ExportStatus.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePreviewIsShownBeforeThePathIsAskedFor()
    {
        // Order matters. Asking where to put it first invites the reader to treat the preview as a
        // formality standing between them and a file they have already decided to write.
        _capture.Arm("vb6", true);
        Frame("vb6", "initialize");
        PreviewAccepted();
        _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns((string?)null);

        var vm = Sut();
        await vm.ExportAsync();

        Received.InOrder(() =>
        {
            _windows.ShowDialog(Arg.Any<IDialog>());
            _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>());
        });
    }

    // ── The pane's preview of what would leave ───────────────────────────────

    [Fact]
    public void ThePaneShowsTheRawBytesUntilAskedOtherwise()
    {
        // Raw is what the window is for. The redaction governs egress, and the pane is not egress.
        _capture.Arm("vb6", true);
        Frame("vb6", "textDocument/didOpen");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.ShowsWhatWouldBeShared.Should().BeFalse();
        vm.PaneText.Should().Be(vm.SelectedBody);
    }

    [Fact]
    public void ThePaneCanShowWhatWouldLeaveInstead()
    {
        _capture.Arm("vb6", true);
        Frame("vb6", "textDocument/didOpen");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();
        vm.ShowsWhatWouldBeShared = true;

        vm.PaneText.Should().Be(vm.SelectedTrace);
        vm.PaneText.Should().Contain("[Trace - ", "the preview is the copy, not a description of it");
    }

    [Fact]
    public void ThePreviewFollowsTheSelectionRatherThanStickingToOneRow()
    {
        // A toggle left on while the reader moves down the grid must keep previewing, or the state silently
        // means something different from what it did a moment ago.
        _capture.Arm("vb6", true);
        Frame("vb6", "first");
        Frame("vb6", "second");

        var vm = Sut();
        vm.ShowsWhatWouldBeShared = true;
        vm.SelectedRow = vm.Rows[0];
        var first = vm.PaneText;

        vm.SelectedRow = vm.Rows[1];

        vm.PaneText.Should().NotBe(first).And.Contain("second");
    }

    // ── A body that is not what it claims ────────────────────────────────────

    [Fact]
    public void AWellFormedBodyIsNotBadgedAsAnything()
    {
        // Service-to-service traffic parses essentially always. A tick on every message would be a badge
        // nobody reads beside the one that matters, so silence means valid.
        _capture.Arm("vb6", true);
        Frame("vb6", "textDocument/didOpen");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.HasBodyProblem.Should().BeFalse();
        vm.BodyProblem.Should().BeEmpty();
    }

    [Fact]
    public void AMalformedBodyIsCalledOutWithItsPosition()
    {
        // A server emitting broken JSON is a real defect and this window is where it surfaces. "Invalid
        // JSON" alone is not actionable in a forty-kilobyte frame; the position is what makes it so.
        _capture.Arm("vb6", true);
        var body = System.Text.Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0",""");
        _capture.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            null, "1", body.Length, body);

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.HasBodyProblem.Should().BeTrue();
        vm.BodyProblem.Should().Contain("Str.Tool.ProtocolInspector.NotJson");
    }

    [Fact]
    public void TheBodyIsStillShownWhenItDoesNotParse()
    {
        // The marker reports; it never withholds. A body this client could not decode is exactly what an
        // author needs in front of them.
        _capture.Arm("vb6", true);
        var body = System.Text.Encoding.UTF8.GetBytes("not json at all");
        _capture.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            null, "1", body.Length, body);

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.HasSelectedBody.Should().BeTrue();
        vm.PaneText.Should().Contain("not json at all");
    }

    [Fact]
    public async Task ATruncatedBodyIsNotAccusedOfBeingMalformed()
    {
        // It is not meant to parse and already says so. Reporting it as broken would be the inspector
        // misreading its own marker as the server's output.
        await using var small = new ConversationLog(new CaptureLimits(FrameBytes: 1024));
        small.Arm("vb6", true);

        var body = System.Text.Encoding.UTF8.GetBytes(
            $$$"""{"jsonrpc":"2.0","result":"{{{new string('x', 4000)}}}"}""");
        small.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            null, "1", body.Length, body);

        var vm = Over(small);
        vm.SelectedRow = vm.Rows.Single();

        vm.HasBodyProblem.Should().BeFalse();
    }

    [Fact]
    public void DeselectingClearsTheMarker()
    {
        _capture.Arm("vb6", true);
        var body = System.Text.Encoding.UTF8.GetBytes("{oops");
        _capture.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            null, "1", body.Length, body);

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();
        vm.HasBodyProblem.Should().BeTrue();

        vm.SelectedRow = null;

        vm.HasBodyProblem.Should().BeFalse();
    }

    [Fact]
    public void AReadableBodyIsIndentedRatherThanLeftAsOneLine()
    {
        // A frame is one line of JSON, and 569 bytes of handshake wrapped across four lines is the thing
        // people copy out to a formatter — which is the outcome this pane exists to prevent. Only
        // insignificant whitespace changes; the true byte count is in the grid beside it.
        _capture.Arm("vb6", true);
        Frame("vb6", "textDocument/didOpen");

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.SelectedBody.Should().Contain(Environment.NewLine[^1..],
            "an indented body has lines, which is what makes it foldable");
        vm.SelectedBody.Should().Contain("textDocument/didOpen", "and it is still the same message");
    }

    [Fact]
    public void ABodyThatWillNotParseIsShownByteForByte()
    {
        // That body is the finding. Reformatting must never become a reason it cannot be read, and a
        // best-effort tidy would misrepresent exactly the bytes somebody is trying to see.
        _capture.Arm("vb6", true);
        var body = System.Text.Encoding.UTF8.GetBytes("""{"a":1,   "b":oops}""");
        _capture.Record("vb6", ConversationDirection.Received, ConversationEntryKind.Response,
            null, "1", body.Length, body);

        var vm = Sut();
        vm.SelectedRow = vm.Rows.Single();

        vm.SelectedBody.Should().Be("""{"a":1,   "b":oops}""");
    }
}
