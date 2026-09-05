using AvaloniaEdit.Document;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using LspRange = HexIDE.Lsp.Messages.Range;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The document lifecycle both editors share: opened, synchronized, closed, and diagnostics turned into
/// markers.
///
/// <para>
/// This exists as its own type because the VB6 code editor and the carried-file editor have almost nothing
/// else in common, and the parts they do share are the parts that were hardest to get right. Those parts
/// are what these tests pin: matching a URI a server has renamed, and converting a range the server
/// measured against text the buffer has since moved past.
/// </para>
/// </summary>
public class LspDocumentSessionTests : IDisposable
{
    private const string Uri = "file:///c:/proj/README.md";

    private readonly ILspClient _client = Substitute.For<ILspClient>();
    private readonly TextDocument _document = new();
    private readonly List<LspDocumentSession> _sessions = [];

    public LspDocumentSessionTests() => _client.IsRunning.Returns(true);

    public void Dispose()
    {
        foreach (var session in _sessions) session.Dispose();
        GC.SuppressFinalize(this);
    }

    private LspDocumentSession Session(string text = "", string uri = Uri)
    {
        _document.Text = text;
        // Marshalling runs inline. Only the thread that initialised Avalonia may pump its dispatcher, and
        // in a suite this size that is whichever test class got there first — so pumping from here passes
        // alone and throws "a different thread owns it" in the full run. Injecting the hop removes the
        // dependency rather than racing it.
        var session = new LspDocumentSession(_client, _document, uri, work => work());
        _sessions.Add(session);
        return session;
    }

    private static PublishDiagnosticsParams OneDiagnostic(
        string uri, int line, int startCharacter, int endCharacter,
        string message = "boom", DiagnosticSeverity? severity = null)
        => new(uri, [new Diagnostic(
            new LspRange(new Position(line, startCharacter), new Position(line, endCharacter)),
            message, severity)]);

    private void Publish(PublishDiagnosticsParams p)
    {
        _client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(_client, p);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartingOpensTheDocumentUnderItsOwnUri()
    {
        Session("# hi").Start();

        await _client.Received(1).OpenDocumentAsync(Uri, "# hi", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheDocumentIsOpenedEvenWhileNoServerIsRunning()
    {
        // Not an omission. Opening TRACKS the document before it looks for a live server, and tracked
        // documents are replayed after a connect — so a gate here would mean a file opened while the
        // server was down is never tracked and never replayed. It is also the trigger that starts a lazily
        // started server, so gating would leave nothing to do the starting.
        _client.IsRunning.Returns(false);

        Session("# hi").Start();

        await _client.Received(1).OpenDocumentAsync(Uri, "# hi", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DisposingClosesTheDocument()
    {
        var session = Session("# hi");
        session.Start();

        session.Dispose();

        await _client.Received(1).CloseDocumentAsync(Uri, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASessionThatNeverStartedClosesNothing()
    {
        // An editor can be constructed and disposed without ever opening a document — a file that failed
        // to read, for one. Closing a document the server was never told about invites it to discard state
        // for a URI it may legitimately hold for something else.
        Session("# hi").Dispose();

        await _client.DidNotReceive().CloseDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartingTwiceOpensOnce()
    {
        var session = Session("# hi");

        session.Start();
        session.Start();

        await _client.Received(1).OpenDocumentAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    // ── Synchronization ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FlushingSendsTheCurrentTextImmediately()
    {
        var session = Session("# hi");
        session.Start();
        _document.Text = "# hi there";

        await session.FlushAsync();

        await _client.Received(1).ChangeDocumentAsync(
            Uri, Arg.Any<int>(), "# hi there", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachFlushCarriesAHigherVersion()
    {
        // The version is the server's only way to order what it receives. A repeated number lets a server
        // legitimately discard the newer text as stale.
        var session = Session("a");
        session.Start();
        var versions = new List<int>();
        _client.When(c => c.ChangeDocumentAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(call => versions.Add(call.ArgAt<int>(1)));

        await session.FlushAsync();
        await session.FlushAsync();

        versions.Should().HaveCount(2);
        versions[1].Should().BeGreaterThan(versions[0]);
    }

    [Fact]
    public async Task ADisposedSessionSendsNoMoreChanges()
    {
        var session = Session("a");
        session.Start();
        session.Dispose();
        _client.ClearReceivedCalls();

        await session.FlushAsync();

        await _client.DidNotReceive().ChangeDocumentAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASaveSendsThePendingEditFirst()
    {
        // The ordering the whole announcement depends on. Editing is debounced, so at the moment a save
        // happens the server may still hold the text from before the last few keystrokes — which is the
        // very text that was just written to disk. Announcing a save against that describes a file the
        // server cannot see, and it does so in the worst way: it looks like it worked.
        var session = Session("before");
        session.Start();
        _document.Text = "after";   // arms the debounce; nothing has been sent yet

        await session.NotifySavedAsync();

        Received.InOrder(() =>
        {
            _client.ChangeDocumentAsync(Uri, Arg.Any<int>(), "after", Arg.Any<CancellationToken>());
            _client.SaveDocumentAsync(Uri, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ASessionThatNeverStartedAnnouncesNothing()
    {
        // A carried file that failed to read, or a document with no path: there is no open document to
        // announce a save of, and telling a server about one invites it to hold state for a URI it was
        // never given.
        await Session("x").NotifySavedAsync();

        await _client.DidNotReceive().SaveDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ADisposedSessionAnnouncesNothing()
    {
        var session = Session("x");
        session.Start();
        session.Dispose();
        _client.ClearReceivedCalls();

        await session.NotifySavedAsync();

        await _client.DidNotReceive().SaveDocumentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DiagnosticsForThisDocumentBecomeMarkers()
    {
        var session = Session("hello world");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(OneDiagnostic(Uri, 0, 0, 5, "spelling"));

        seen.Should().ContainSingle().Which.Message.Should().Be("spelling");
        seen![0].StartOffset.Should().Be(0);
        seen[0].EndOffset.Should().Be(5);
    }

    [Fact]
    public void DiagnosticsForAnotherDocumentAreIgnored()
    {
        // Every editor hears every publication — there is one channel — so filtering is the only thing
        // stopping one file's errors being drawn in another's buffer.
        var session = Session("hello world");
        var raised = false;
        session.MarkersChanged += _ => raised = true;
        session.Start();

        Publish(OneDiagnostic("file:///c:/proj/OTHER.md", 0, 0, 5));

        raised.Should().BeFalse();
    }

    [Fact]
    public void AUriTheServerPercentEncodedDifferentlyStillMatches()
    {
        // #236, and the reason this comparison is not `!=`. A server is under no obligation to echo a URI
        // back byte-for-byte and conformant ones routinely do not; an exact match then drops every
        // diagnostic it publishes and reports nothing at all — which reads exactly like a server with no
        // opinions.
        //
        // Percent-encoding rather than drive-letter case, because this one holds on every platform: the
        // path is unescaped before comparison regardless of the host filesystem.
        var session = Session("hello world", "file:///c:/proj/read me.md");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(OneDiagnostic("file:///c:/proj/read%20me.md", 0, 0, 5));

        seen.Should().ContainSingle();
    }

    [Fact]
    public void AVb6UriDifferingOnlyInCaseStillMatches()
    {
        // The IDE's own scheme, and the case that matters most for the VB6 editor: a `vb6://` path segment
        // is a VB6 identifier, and VB6 identifiers are case-insensitive on every platform. A server that
        // echoes `module1` for our `Module1` is not disagreeing with us about anything.
        var session = Session("Sub Main()", "vb6://module/Module1");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(OneDiagnostic("vb6://module/module1", 0, 0, 3));

        seen.Should().ContainSingle();
    }

    [WindowsOnlyFact]
    public void AWindowsDriveLetterInTheOtherCaseStillMatches()
    {
        // The literal #236 measurement: a real third-party server answered `file:///c:/…` to our
        // `file:///C:/…`.
        //
        // Windows-only, and that is the PRODUCT's rule rather than a limitation of the test. On a
        // case-sensitive filesystem `/C:/proj` and `/c:/proj` are different paths, and matching them would
        // trade a silently dropped diagnostic for a silently MIS-ATTRIBUTED one — the worse of the two.
        // LspDocumentUri says so explicitly and asks OperatingSystem.IsWindows(). Asserting it everywhere
        // is how this test failed on Linux while the code was right.
        var session = Session("hello world", "file:///c:/proj/README.md");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(OneDiagnostic("file:///C:/proj/README.md", 0, 0, 5));

        seen.Should().ContainSingle();
    }

    [Fact]
    public void AnEmptySetClearsTheMarkers()
    {
        var session = Session("hello world");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();
        Publish(OneDiagnostic(Uri, 0, 0, 5));

        Publish(new PublishDiagnosticsParams(Uri, []));

        seen.Should().BeEmpty("an empty set is how a server says the problems are resolved");
    }

    [Fact]
    public void ADisposedSessionRaisesNothing()
    {
        // The editor is gone; its buffer may be too. Converting offsets against it is at best pointless
        // and at worst an exception on the UI thread.
        var session = Session("hello world");
        var raised = false;
        session.MarkersChanged += _ => raised = true;
        session.Start();

        session.Dispose();
        Publish(OneDiagnostic(Uri, 0, 0, 5));

        raised.Should().BeFalse();
    }

    [Fact]
    public void TheOwnerIsToldWheneverDiagnosticsWereApplied()
    {
        // The code editor refreshes its procedure list off this, because a fresh diagnostic set means the
        // server has evidently just re-read the document. Keeping it an event is what stops this class
        // needing to know what a procedure is.
        var session = Session("hello world");
        var applied = 0;
        session.DiagnosticsApplied += () => applied++;
        session.Start();

        Publish(OneDiagnostic(Uri, 0, 0, 5));

        applied.Should().Be(1);
    }

    [Fact]
    public void WorkAlreadyPostedWhenTheEditorClosesIsAbandoned()
    {
        // The race the inner disposal guard exists for, and the one the rest of this fixture cannot see.
        // Everywhere else the UI hop runs inline, so the guard before the post and the guard inside it are
        // evaluated in one call frame with the same answer. In production the hop is a real dispatcher
        // post: the handler can pass the outer check on a background thread, the editor can close, and the
        // posted work then runs against a buffer whose editor is gone.
        //
        // Reproduced by queuing the posted work instead of running it, closing, and only then draining.
        var queued = new List<Action>();
        _document.Text = "hello world";
        var session = new LspDocumentSession(_client, _document, Uri, queued.Add);
        _sessions.Add(session);
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        _client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            _client, OneDiagnostic(Uri, 0, 0, 5));
        queued.Should().ContainSingle("the conversion is posted, not run where the notification arrived");

        session.Dispose();
        foreach (var work in queued) work();

        seen.Should().BeNull(
            "converting offsets against a buffer whose editor has gone is at best pointless, and the "
          + "markers would be handed to a renderer that is no longer attached");
    }

    // ── Ranges the buffer has moved past ──────────────────────────────────────────────────────────────

    [Fact]
    public void ADiagnosticPastTheEndOfTheBufferIsDropped()
    {
        // Ordinary traffic, not a server fault: with a debounce in flight the server is answering about
        // text the buffer has already moved past. It must not throw.
        var session = Session("one line");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        var publish = () => Publish(OneDiagnostic(Uri, 40, 0, 5));

        publish.Should().NotThrow();
        seen.Should().BeEmpty();
    }

    [Fact]
    public void ADiagnosticRunningOffTheEndOfTheLineIsClamped()
    {
        var session = Session("abc");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(OneDiagnostic(Uri, 0, 0, 999));

        seen.Should().ContainSingle().Which.EndOffset.Should().Be(3, "the buffer is three characters long");
    }

    [Fact]
    public void AZeroWidthDiagnosticStillMarksSomething()
    {
        // A server pointing AT a position rather than spanning one — a missing token, most often. A marker
        // of zero width draws nothing, so there would be no squiggle and no explanation for it.
        var session = Session("abc");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(OneDiagnostic(Uri, 0, 1, 1));

        var marker = seen.Should().ContainSingle().Subject;
        marker.EndOffset.Should().BeGreaterThan(marker.StartOffset);
    }

    [Fact]
    public void ADiagnosticAtTheVeryEndOfTheBufferStaysInsideIt()
    {
        // The shape that needs the end-of-buffer clamp, and the one the obvious "range past the end" test
        // does not reach: the START is still valid — the last offset in the buffer — while the END line is
        // past the document, so the fallback widens it by one and lands outside the text. Found by
        // mutation: deleting the clamp broke no test until this one existed, because AvaloniaEdit already
        // clamps a column within a line that exists, and only this path bypasses it.
        //
        // A marker pointing past the text is not a cosmetic problem. It is handed to the renderer as an
        // offset range to draw.
        var session = Session("abc");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(new PublishDiagnosticsParams(Uri, [new Diagnostic(
            new LspRange(new Position(0, 3), new Position(5, 0)), "past the end")]));

        var marker = seen.Should().ContainSingle().Subject;
        marker.EndOffset.Should().BeLessThanOrEqualTo(3, "the buffer is three characters long");
        marker.StartOffset.Should().BeLessThanOrEqualTo(marker.EndOffset);
    }

    [Fact]
    public void ARangeThatEndsBeforeItStartsIsStillAUsableMarker()
    {
        // A malformed range from a server — end before start. Found by mutation: deleting the
        // start-past-end clamp broke nothing, because the two obvious cases cannot reach it. A zero-width
        // diagnostic on a line that exists is already widened by the Math.Max on the end column, and one
        // at the very last offset stays zero-width either way, since there is no character after it to
        // mark. Only a reversed range gets here.
        //
        // It matters because the result is not merely empty, it is INVERTED: a marker whose end precedes
        // its start, handed to a renderer as an offset range to draw.
        var session = Session("line one\r\nline two");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(new PublishDiagnosticsParams(Uri, [new Diagnostic(
            new LspRange(new Position(1, 0), new Position(0, 0)), "backwards")]));

        var marker = seen.Should().ContainSingle().Subject;
        marker.EndOffset.Should().BeGreaterThan(marker.StartOffset);
    }

    // ── Severity ──────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData(DiagnosticSeverity.Error, true)]
    [InlineData(DiagnosticSeverity.Warning, false)]
    public void AnUnstatedSeverityIsTreatedAsAnError(DiagnosticSeverity? severity, bool expectedError)
    {
        // The protocol leaves an omitted severity to the client. Treating it as a hint would silently
        // downgrade everything from a server that never sets the field — and a foreign server is exactly
        // where that assumption stops holding.
        var session = Session("abc");
        IReadOnlyList<LspMarker>? seen = null;
        session.MarkersChanged += m => seen = m;
        session.Start();

        Publish(OneDiagnostic(Uri, 0, 0, 3, severity: severity));

        seen.Should().ContainSingle().Which.IsError.Should().Be(expectedError);
    }
}
