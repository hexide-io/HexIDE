using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

// `Range` is ambiguous here — HexIDE.Lsp.Messages.Range vs System.Range.
using LspRange = HexIDE.Lsp.Messages.Range;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

public class LspClientRegistryTests
{
    private const string Vb6Doc = "vb6://module/Module1";

    // A server advertising the full standard set. Since capability gating landed, a fake advertising
    // NOTHING serves nothing — which is correct, and means these fakes must say what they support.
    private const string FullCapabilities = """
        {"textDocumentSync":{"openClose":true,"change":1},"hoverProvider":true,
         "documentSymbolProvider":true,"foldingRangeProvider":true,"completionProvider":{},
         "signatureHelpProvider":{},"definitionProvider":true,"documentHighlightProvider":true,
         "renameProvider":true,"documentFormattingProvider":true}
        """;

    private static ILspClient FakeServer(bool running = true, string? capabilitiesJson = FullCapabilities)
    {
        var c = Substitute.For<ILspClient>();
        c.IsRunning.Returns(running);
        c.AdvertisedCapabilities.Returns(
            capabilitiesJson is null ? null : JsonDocument.Parse(capabilitiesJson).RootElement.Clone());
        c.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        c.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([]);
        c.RequestBuiltinSymbolsAsync(Arg.Any<CancellationToken>()).Returns([]);
        return c;
    }

    // Extensions and language id are now separate claims: a server says which files it wants, and
    // separately what it wants them called. These tests route through the vb6:// scheme, which matches on
    // the language id, so the extensions are only here to make each registration well-formed.
    private static LanguageServerRegistration Registration(
        string id, ILspClient client, string language = DocumentLanguage.Vb6, int priority = 0) =>
        new(id, id,
            language == DocumentLanguage.Vb6 ? DocumentLanguage.Vb6Extensions : [".md", ".markdown"],
            language, () => client, priority);

    private static LspClientRegistry Registry(params LanguageServerRegistration[] registrations) =>
        new(registrations, Substitute.For<ILogger<LspClientRegistry>>());

    private static LspRange Span(int line) => new(new Position(line, 0), new Position(line, 1));

    [Fact]
    public async Task OnlyServersClaimingTheDocumentsLanguageAreStarted()
    {
        // The whole point of lazy start: a project with no Markdown must not pay for a Markdown server.
        var vb6 = FakeServer();
        var markdown = FakeServer();
        var sut = Registry(
            Registration("vb6", vb6),
            Registration("md", markdown, language: "markdown"));

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\nEnd Sub", TestContext.Current.CancellationToken);

        await vb6.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await markdown.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsyncOnTheRegistryStartsNothing()
    {
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await server.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        sut.Connections.Single().State.Should().Be(LanguageConnectionState.NotStarted);
    }

    [Fact]
    public async Task EveryClaimantSeesTheDocumentAndTheirCompletionsMerge()
    {
        // The merge rule, kept where it belongs. Two servers offering different candidates for one prefix
        // are adding to each other, and dropping either would hide something the user could have typed.
        var first = FakeServer();
        var second = FakeServer();
        first.RequestCompletionAsync(Vb6Doc, Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns([new CompletionItem("A", CompletionItemKind.Function)]);
        second.RequestCompletionAsync(Vb6Doc, Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns([new CompletionItem("B", CompletionItemKind.Function)]);

        var sut = Registry(Registration("a", first), Registration("b", second));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        var items = await sut.RequestCompletionAsync(
            Vb6Doc, new Position(0, 0), TestContext.Current.CancellationToken);

        items.Select(i => i.Label).Should().BeEquivalentTo(["A", "B"],
            "a language server beside a linter is ordinary, not exotic — both answers belong");
    }

    [Fact]
    public async Task DocumentSymbolsComeFromOneServer_NotEveryClaimant()
    {
        // THIS REPLACES A MERGE. The rule used to be that symbols from every claimant were concatenated,
        // on the same "a language server beside a linter" reasoning as completions. It does not hold here,
        // for two reasons found later: a linter does not advertise `documentSymbolProvider`, so it was
        // never a claimant for this call — and a document has ONE structure, so two servers that DO
        // advertise it are describing the same procedures twice, not contributing different ones.
        //
        // The visible cost was every procedure appearing twice in the VB6 procedure dropdown the moment a
        // second VB6-capable server was attached, which is precisely the configuration this seam exists
        // to make ordinary.
        var first = FakeServer();
        var second = FakeServer();
        first.RequestDocumentSymbolsAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new DocumentSymbol("A", SymbolKind.Function, Span(0), Span(0))]);
        second.RequestDocumentSymbolsAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new DocumentSymbol("A", SymbolKind.Function, Span(0), Span(0))]);

        var sut = Registry(Registration("a", first, priority: 5), Registration("b", second));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        var symbols = await sut.RequestDocumentSymbolsAsync(Vb6Doc, TestContext.Current.CancellationToken);

        symbols.Select(s => s.Name).Should().BeEquivalentTo(["A"],
            "both servers describe the same document, so merging duplicates rather than adds");
        await second.DidNotReceive().RequestDocumentSymbolsAsync(Vb6Doc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FoldsComeFromOneServerToo()
    {
        // Sharper than the symbol case: AvaloniaEdit's FoldingManager takes one ordered set of sections,
        // and two servers' folds overlap partially in general. A partially-overlapping section is not a
        // cosmetic duplicate — it can take out folding for the whole document.
        var first = FakeServer();
        var second = FakeServer();
        first.RequestFoldingRangesAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new FoldingRange(0, 10)]);
        second.RequestFoldingRangesAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new FoldingRange(5, 15)]);

        var sut = Registry(Registration("a", first, priority: 5), Registration("b", second));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        var folds = await sut.RequestFoldingRangesAsync(Vb6Doc, TestContext.Current.CancellationToken);

        folds.Should().ContainSingle().Which.EndLine.Should().Be(10);
        await second.DidNotReceive().RequestFoldingRangesAsync(Vb6Doc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FormattingGoesToExactlyOneServer()
    {
        // Two sets of edits to one document cannot both be applied. A second server's edits are not a
        // fallback; they are a different opinion about the same text.
        var low = FakeServer();
        var high = FakeServer();
        high.RequestFormattingAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new TextEdit(Span(0), "x")]);

        var sut = Registry(Registration("low", low, priority: 0), Registration("high", high, priority: 10));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        var edits = await sut.RequestFormattingAsync(Vb6Doc, TestContext.Current.CancellationToken);

        edits.Should().HaveCount(1);
        await low.DidNotReceive().RequestFormattingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FormattingGoesToTheServerThatOFFERSIt_NotTheTopPriorityOne()
    {
        // Selection for a pick-one feature is among servers that advertise it. Otherwise a higher-priority
        // server with no formatter silently blocks a lower one that has it — and the user sees formatting
        // do nothing, with a perfectly healthy formatter installed.
        var cannotFormat = FakeServer(capabilitiesJson: """{"hoverProvider":true}""");
        var canFormat = FakeServer(capabilitiesJson: """{"documentFormattingProvider":true}""");
        canFormat.RequestFormattingAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new TextEdit(Span(0), "formatted")]);

        var sut = Registry(
            Registration("cannot", cannotFormat, priority: 10),
            Registration("can", canFormat));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        var edits = await sut.RequestFormattingAsync(Vb6Doc, TestContext.Current.CancellationToken);

        edits.Should().ContainSingle().Which.NewText.Should().Be("formatted");
        await cannotFormat.DidNotReceive().RequestFormattingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EqualPrioritiesFallBackToRegistrationOrder()
    {
        var first = FakeServer();
        var second = FakeServer();
        var sut = Registry(Registration("first", first), Registration("second", second));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        await sut.RequestFormattingAsync(Vb6Doc, TestContext.Current.CancellationToken);

        await first.Received(1).RequestFormattingAsync(Vb6Doc, Arg.Any<CancellationToken>());
        await second.DidNotReceive().RequestFormattingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HoverTakesTheFirstServerThatActuallyAnswers()
    {
        // Highest priority wins only if it has something to say — otherwise a silent top-priority server
        // would mask a lower one that does.
        var silent = FakeServer();
        var talkative = FakeServer();
        silent.RequestHoverAsync(Vb6Doc, Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns((HoverResult?)null);
        talkative.RequestHoverAsync(Vb6Doc, Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new HoverResult(new MarkupContent("plaintext", "hello"), null));

        var sut = Registry(Registration("silent", silent, priority: 10), Registration("talkative", talkative));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        var hover = await sut.RequestHoverAsync(Vb6Doc, new Position(0, 0), TestContext.Current.CancellationToken);

        hover!.Contents.Value.Should().Be("hello");
    }

    [Fact]
    public async Task BuiltinSymbolsRouteByAdvertisedCapabilityNotByLanguage()
    {
        // It has no document to route by, so the server that DECLARES it is the one that can answer.
        var without = FakeServer(capabilitiesJson: """{"hoverProvider":true}""");
        var with = FakeServer(capabilitiesJson: """{"experimental":{"vbBuiltinSymbols":true}}""");
        with.RequestBuiltinSymbolsAsync(Arg.Any<CancellationToken>())
            .Returns([new VbaBuiltinSymbol("Len", "Len(s)", "length")]);

        var sut = Registry(Registration("without", without, priority: 10), Registration("with", with));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        var symbols = await sut.RequestBuiltinSymbolsAsync(TestContext.Current.CancellationToken);

        symbols.Should().ContainSingle().Which.Name.Should().Be("Len");
    }

    [Fact]
    public async Task AnUnrecognisedLanguageStartsNothingAndReturnsEmpty()
    {
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));

        await sut.OpenDocumentAsync("file:///c:/proj/notes.xyz", "whatever", TestContext.Current.CancellationToken);
        var symbols = await sut.RequestDocumentSymbolsAsync("file:///c:/proj/notes.xyz", TestContext.Current.CancellationToken);

        await server.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        symbols.Should().BeEmpty("an unrecognised document opens with features absent, not with an error");
    }

    [Fact]
    public async Task AServerThatFailsToStartIsMarkedFailedAndNotRetried()
    {
        // Retrying on every document open turns one broken registration into a cost the user pays
        // repeatedly, on a path where nothing has changed to make the next attempt more likely to work.
        var server = Substitute.For<ILspClient>();
        server.StartAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("no")));
        var sut = Registry(Registration("broken", server));

        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Vb6Doc, "code again", TestContext.Current.CancellationToken);

        sut.Connections.Single().State.Should().Be(LanguageConnectionState.Failed);
        await server.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectionsReportNotStartedBeforeUseAndRunningAfter()
    {
        // A server that is quiet because nothing triggered it must be distinguishable from one that is
        // missing or broken — that distinction is most of what a connections view is for.
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));

        sut.Connections.Single().State.Should().Be(LanguageConnectionState.NotStarted);

        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        sut.Connections.Single().State.Should().Be(LanguageConnectionState.Running);
    }

    [Fact]
    public async Task AServerThatDiesAfterStartingStopsReportingItselfRunning()
    {
        // The projection used to read the CACHED state on one line and the LIVE capabilities on the
        // next. Entry.State is written in five places and none is a death path -- when a transport
        // closes, the client clears its own flags and tells the registry nothing -- so a crashed server
        // reported Running, for the rest of the session, beside capabilities that had already gone null.
        // Running-with-nothing-advertised is the most confusing row this record can produce.
        var alive = true;
        var server = FakeServer();
        server.IsRunning.Returns(_ => alive);
        server.AdvertisedCapabilities.Returns(_ => alive
            ? JsonDocument.Parse(FullCapabilities).RootElement.Clone()
            : (JsonElement?)null);
        var sut = Registry(Registration("vb6", server));

        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);
        sut.Connections.Single().State.Should().Be(LanguageConnectionState.Running);

        alive = false;   // the far end went away; nothing writes that down

        var row = sut.Connections.Single();
        row.State.Should().Be(LanguageConnectionState.Stopped,
            "a server that has gone away is Stopped, not Running -- and reporting Running beside null "
          + "capabilities is the pairing that makes a connections view actively misleading");
        row.Capabilities.Should().BeNull("the live read already knew; only the state lagged");
    }

    [Fact]
    public async Task AServerWhoseReconnectSucceededStopsReportingItselfFailed()
    {
        // The same staleness read the other way. A pipe or websocket client that comes back up through
        // its own reconnect loop is live again, while the cached state still says Failed from the first
        // attempt. Fixing only the Running direction would leave this half wrong.
        var alive = false;
        var server = FakeServer();
        server.IsRunning.Returns(_ => alive);
        var sut = Registry(Registration("vb6", server));

        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);
        sut.Connections.Single().State.Should().Be(LanguageConnectionState.Failed);

        alive = true;    // the reconnect loop got through

        sut.Connections.Single().State.Should().Be(LanguageConnectionState.Running);
    }

    [Fact]
    public async Task TheAggregateAndThePerConnectionStateCannotDisagree()
    {
        // IsRunning reads the client live; Connections[].State used to read the cache. The registry could
        // therefore contradict itself -- IsRunning false while the only row said Running. Whatever the
        // answer is, one object must not give two of them.
        var alive = true;
        var server = FakeServer();
        server.IsRunning.Returns(_ => alive);
        var sut = Registry(Registration("vb6", server));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        foreach (var live in new[] { true, false, true })
        {
            alive = live;
            var anyRunning = sut.Connections.Any(c => c.State == LanguageConnectionState.Running);
            anyRunning.Should().Be(sut.IsRunning,
                $"the aggregate and the rows must agree (client alive: {live})");
        }
    }

    [Fact]
    public async Task ReportingHonestlyDoesNotMakeAFailedServerRetry()
    {
        // The guard on the fix itself. Reconciling the PROJECTION must not repeal the invariant that a
        // failed server stays failed for the session -- that is an argued decision (retrying on every
        // document open turns one broken registration into a repeated cost), and a projection change is
        // exactly the kind of edit that could undo it by accident.
        var server = Substitute.For<ILspClient>();
        server.IsRunning.Returns(false);
        server.StartAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("no")));
        var sut = Registry(Registration("broken", server));

        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Vb6Doc, "more", TestContext.Current.CancellationToken);

        sut.Connections.Single().State.Should().Be(LanguageConnectionState.Failed);
        await server.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InjectedDiagnosticsAreRaisedWithNoServerAtAll()
    {
        // The external-compiler side channel. It must not depend on a language server, because the whole
        // point is that it comes from somewhere else.
        var sut = Registry(Registration("vb6", FakeServer()));
        PublishDiagnosticsParams? seen = null;
        sut.DiagnosticsPublished += (_, p) => seen = p;

        await sut.InjectDiagnosticsAsync(Vb6Doc, []);

        seen.Should().NotBeNull();
        seen!.Uri.Should().Be(Vb6Doc);
    }

    [Fact]
    public async Task DiagnosticsFromAnyServerReachSubscribers()
    {
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));
        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        PublishDiagnosticsParams? seen = null;
        sut.DiagnosticsPublished += (_, p) => seen = p;
        server.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            server, new PublishDiagnosticsParams(Vb6Doc, []));

        seen.Should().NotBeNull("a diagnostic from any connection is still a diagnostic for that document");
    }

    [Fact]
    public async Task IsRunningMeansAnyConnectionIsUp()
    {
        var sut = Registry(Registration("vb6", FakeServer()));

        sut.IsRunning.Should().BeFalse("nothing has started yet");

        await sut.OpenDocumentAsync(Vb6Doc, "code", TestContext.Current.CancellationToken);

        sut.IsRunning.Should().BeTrue();
    }

    [Theory]
    [InlineData("vb6://module/Module1", DocumentLanguage.Vb6)]   // scheme, no extension at all
    [InlineData("vb6://form/Form1", DocumentLanguage.Vb6)]
    [InlineData("VB6://module/M", DocumentLanguage.Vb6)]         // scheme is case-insensitive
    [InlineData("file:///c:/p/Mod.bas", null)]                   // `file` names a transport, not a language
    [InlineData("custom://thing/x", null)]                       // an unknown scheme claims nothing
    [InlineData("", null)]
    [InlineData(null, null)]
    public void OnlyHexIdesOwnSchemeNamesALanguage(string? uri, string? expected)
    {
        // Scheme first is load-bearing rather than tidy: HexIDE's own documents are vb6://module/Module1,
        // which carry no extension, so an extension-only rule would fail to classify the only documents the
        // IDE opens today. It stays in code because the scheme is HexIDE's invention, not a server's claim.
        DocumentLanguage.SchemeLanguageOf(uri).Should().Be(expected);
    }

    [Theory]
    [InlineData("file:///c:/p/Mod.bas", ".bas")]
    [InlineData("file:///c:/p/Form.FRM", ".frm")]               // normalised, so claims compare case-blind
    [InlineData("file:///c:/p/README.md", ".md")]
    [InlineData("file:///c:/p/a.bas?v=2", ".bas")]              // a URI may carry a query
    [InlineData("file:///c:/p/a.bas#frag", ".bas")]
    [InlineData("file:///c:/p/no-extension", null)]
    [InlineData("file:///c:/p.d/no-extension", null)]           // the dot is in a DIRECTORY, not the name
    [InlineData("vb6://module/Module1", null)]                  // nothing to extract, hence scheme-first
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AnExtensionIsExtractedWithoutBeingInterpreted(string? uri, string? expected)
    {
        // Deliberately no mapping to a language. Which server wants a .md is a claim servers make; this only
        // supplies the key they are compared against.
        DocumentLanguage.ExtensionOf(uri).Should().Be(expected);
    }
}
