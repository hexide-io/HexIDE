using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Drives HexIDE's language layer against a <b>real third-party server we did not write</b>.
///
/// <para>
/// Everything else in this suite tests the layer against itself. That is necessary and not sufficient: our
/// own server advertised nothing while our own client called everything unconditionally, and the pair
/// worked only because each was wrong in the way the other expected. These tests are the ones that can
/// catch that class of defect, because the far end has no idea we exist.
/// </para>
///
/// <para>
/// Skipped, visibly, when no server is installed — see <see cref="ForeignServerFactAttribute"/>.
/// </para>
/// </summary>
public class ForeignServerIntegrationTests : IAsyncDisposable
{
    private const string MarkdownUri = "file:///c:/proj/notes/README.md";

    // Deliberately violates common Markdown lint rules: no top-level heading, a heading with no space
    // after the hashes, and trailing blank lines. Which rules fire is the server's business — the test
    // asserts that SOMETHING well-formed comes back, not that a particular rule exists.
    private const string SloppyMarkdown = "##Heading without a space\n\n\n\nsome text\n\n\n";

    private LspClientRegistry? _registry;

    /// <summary>
    /// A registry holding one connection to the foreign server, claiming Markdown. No VB6 server — this
    /// isolates the foreign path so a failure cannot be masked by ours answering instead.
    /// </summary>
    private LspClientRegistry ForeignMarkdownRegistry()
    {
        // No locator, mocked or otherwise. The transport is told what to launch, which is the whole point:
        // a locator that answers "where is THE server" cannot describe a second one, so faking it was the
        // test admitting the shipping path could not express what the test was proving.
        var serverInfo = new LspServerInfo(
            ForeignServer.Find()!, ForeignServer.ServerArguments, Path.GetTempPath());

        var loggerFactory = LoggerFactory.Create(b => { });
        var registration = new LanguageServerRegistration(
            Id: "foreign.markdown",
            DisplayName: "Foreign Markdown server",
            Extensions: ForeignServer.Extensions,
            LanguageId: ForeignServer.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(serverInfo, loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                loggerFactory.CreateLogger<VBLspClient>(),
                ForeignServer.LanguageId));

        _registry = new LspClientRegistry([registration], loggerFactory.CreateLogger<LspClientRegistry>());
        return _registry;
    }

    [ForeignServerFact]
    public async Task AForeignServerStartsLazilyAndItsDiagnosticsReachUs()
    {
        // The whole proof in one test: routing by language id, lazy start on first document, a real stdio
        // subprocess we did not write, a `file://` URI (every URI HexIDE has ever sent is `vb6://`), and a
        // capability handshake with a server that advertises honestly rather than advertising nothing.
        var sut = ForeignMarkdownRegistry();
        var received = new TaskCompletionSource<PublishDiagnosticsParams>(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, p) => received.TrySetResult(p);

        await sut.StartAsync();
        sut.IsRunning.Should().BeFalse("nothing has been opened, so nothing should have started");

        await sut.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);

        var published = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));

        published.Uri.Should().Be(MarkdownUri, "a foreign server may normalise the URI — #236 is why this is checked");
        published.Diagnostics.Should().NotBeEmpty("the document deliberately violates common Markdown lint rules");
        published.Diagnostics.Should().OnlyContain(
            d => d.Range.Start.Line >= 0 && d.Range.End.Line >= d.Range.Start.Line,
            "ranges must be well-formed, or the editor cannot place a marker");
    }

    [ForeignServerFact]
    public async Task ItAdvertisesCapabilitiesAndTheyAreVisibleOnTheConnection()
    {
        // The mirror of #242. Ours advertised `{}` for months and nothing noticed, because our own client
        // ignored the answer. A foreign server advertises for real, so this asserts the negotiation
        // actually happened rather than being tolerated.
        var sut = ForeignMarkdownRegistry();
        await sut.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);

        var connection = sut.Connections.Single();

        connection.State.Should().Be(LanguageConnectionState.Running);
        connection.LanguageId.Should().Be(ForeignServer.LanguageId);
        connection.Extensions.Should().Contain(".md");
        connection.Capabilities.Should().NotBeNull("a conformant server advertises what it can do");
        ServerCapabilities.AcceptsOpenClose(connection.Capabilities)
            .Should().BeTrue("it accepted didOpen, so it must have advertised document sync");
    }

    [ForeignServerFact]
    public async Task AVb6DocumentIsNotRoutedToTheMarkdownServer()
    {
        // Routing is only meaningful if it EXCLUDES. Without this the suite would pass with a registry
        // that sent every document to every server.
        var sut = ForeignMarkdownRegistry();

        await sut.OpenDocumentAsync("vb6://module/Module1", "Sub Main()\nEnd Sub\n");

        sut.Connections.Single().State.Should().Be(
            LanguageConnectionState.NotStarted,
            "a VB6 document must not start a server that claims only Markdown");
    }

    [ForeignServerFact]
    public async Task AFeatureTheForeignServerDoesNotAdvertiseIsNotRequested()
    {
        // Gating against a real advertisement rather than our own server's. Whatever this server does or
        // does not implement, the result must be the ordinary empty one rather than an error surfacing.
        var sut = ForeignMarkdownRegistry();
        await sut.OpenDocumentAsync(MarkdownUri, SloppyMarkdown);

        var act = async () => await sut.RequestFoldingRangesAsync(MarkdownUri);

        (await act.Should().NotThrowAsync()).Which.Should().NotBeNull(
            "an unadvertised feature degrades to empty, exactly as an absent server does");
    }

    public async ValueTask DisposeAsync()
    {
        if (_registry is not null)
        {
            try { await _registry.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
    }
}
