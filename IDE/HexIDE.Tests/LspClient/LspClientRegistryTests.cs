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

    private static LanguageServerRegistration Registration(
        string id, ILspClient client, string language = DocumentLanguage.Vb6, int priority = 0) =>
        new(id, id, [language], () => client, priority);

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

        await sut.StartAsync();
        await sut.OpenDocumentAsync(Vb6Doc, "Sub Main()\nEnd Sub");

        await vb6.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await markdown.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsyncOnTheRegistryStartsNothing()
    {
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));

        await sut.StartAsync();

        await server.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        sut.Connections.Single().State.Should().Be(LanguageConnectionState.NotStarted);
    }

    [Fact]
    public async Task EveryClaimantSeesTheDocumentAndTheirResultsMerge()
    {
        var first = FakeServer();
        var second = FakeServer();
        first.RequestDocumentSymbolsAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new DocumentSymbol("A", SymbolKind.Function, Span(0), Span(0))]);
        second.RequestDocumentSymbolsAsync(Vb6Doc, Arg.Any<CancellationToken>())
            .Returns([new DocumentSymbol("B", SymbolKind.Function, Span(1), Span(1))]);

        var sut = Registry(Registration("a", first), Registration("b", second));
        await sut.OpenDocumentAsync(Vb6Doc, "code");

        var symbols = await sut.RequestDocumentSymbolsAsync(Vb6Doc);

        symbols.Select(s => s.Name).Should().BeEquivalentTo(["A", "B"],
            "a language server beside a linter is ordinary, not exotic — both answers belong");
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
        await sut.OpenDocumentAsync(Vb6Doc, "code");

        var edits = await sut.RequestFormattingAsync(Vb6Doc);

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
        await sut.OpenDocumentAsync(Vb6Doc, "code");

        var edits = await sut.RequestFormattingAsync(Vb6Doc);

        edits.Should().ContainSingle().Which.NewText.Should().Be("formatted");
        await cannotFormat.DidNotReceive().RequestFormattingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EqualPrioritiesFallBackToRegistrationOrder()
    {
        var first = FakeServer();
        var second = FakeServer();
        var sut = Registry(Registration("first", first), Registration("second", second));
        await sut.OpenDocumentAsync(Vb6Doc, "code");

        await sut.RequestFormattingAsync(Vb6Doc);

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
        await sut.OpenDocumentAsync(Vb6Doc, "code");

        var hover = await sut.RequestHoverAsync(Vb6Doc, new Position(0, 0));

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
        await sut.OpenDocumentAsync(Vb6Doc, "code");

        var symbols = await sut.RequestBuiltinSymbolsAsync();

        symbols.Should().ContainSingle().Which.Name.Should().Be("Len");
    }

    [Fact]
    public async Task AnUnrecognisedLanguageStartsNothingAndReturnsEmpty()
    {
        var server = FakeServer();
        var sut = Registry(Registration("vb6", server));

        await sut.OpenDocumentAsync("file:///c:/proj/notes.xyz", "whatever");
        var symbols = await sut.RequestDocumentSymbolsAsync("file:///c:/proj/notes.xyz");

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

        await sut.OpenDocumentAsync(Vb6Doc, "code");
        await sut.OpenDocumentAsync(Vb6Doc, "code again");

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

        await sut.OpenDocumentAsync(Vb6Doc, "code");

        sut.Connections.Single().State.Should().Be(LanguageConnectionState.Running);
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
        await sut.OpenDocumentAsync(Vb6Doc, "code");

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

        await sut.OpenDocumentAsync(Vb6Doc, "code");

        sut.IsRunning.Should().BeTrue();
    }

    [Theory]
    [InlineData("vb6://module/Module1", DocumentLanguage.Vb6)]   // scheme, no extension at all
    [InlineData("vb6://form/Form1", DocumentLanguage.Vb6)]
    [InlineData("VB6://module/M", DocumentLanguage.Vb6)]         // scheme is case-insensitive
    [InlineData("file:///c:/p/Mod.bas", DocumentLanguage.Vb6)]   // extension fallback
    [InlineData("file:///c:/p/Form.FRM", DocumentLanguage.Vb6)]
    [InlineData("file:///c:/p/README.md", null)]                 // recognised file, no server for it yet
    [InlineData("file:///c:/p/no-extension", null)]
    [InlineData("custom://thing/x", null)]                       // unknown scheme names no language
    [InlineData("", null)]
    [InlineData(null, null)]
    public void DocumentLanguage_ClassifiesSchemeFirstThenExtension(string? uri, string? expected)
    {
        // Scheme first is load-bearing rather than tidy: HexIDE's own documents are vb6://module/Module1,
        // which carry no extension, so an extension-only rule would fail to classify the only language
        // currently served.
        DocumentLanguage.Of(uri).Should().Be(expected);
    }
}
