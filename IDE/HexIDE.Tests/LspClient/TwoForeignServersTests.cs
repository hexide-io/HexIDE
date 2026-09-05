using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Two language servers by different authors, attached at once.
///
/// <para>
/// One foreign server established that HexIDE can talk to something it did not write. A second
/// establishes that it was not accidentally shaped around that one server's habits — which is a different
/// claim, and the only way to test it is with a server that shares no code, no author and no assumptions
/// with the first.
/// </para>
///
/// <para>
/// It also makes the <c>.cls</c> collision real. A <c>.cls</c> is a VB6 class module and a LaTeX class
/// file both; the routing rule that a lone <c>.cls</c> claim does not mean "serves VB6" was reasoned
/// about with a hypothetical LaTeX server, and there is now an actual one to reason with.
/// </para>
/// </summary>
public class TwoForeignServersTests : IAsyncDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-two-" + Guid.NewGuid().ToString("N"));

    private readonly List<LspClientRegistry> _registries = [];
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => { });

    public TwoForeignServersTests() => Directory.CreateDirectory(_dir);

    public async ValueTask DisposeAsync()
    {
        foreach (var registry in _registries)
        {
            try { await registry.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        _loggerFactory.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private LanguageServerRegistration RegistrationFor(ForeignServer server, string id)
    {
        var info = new LspServerInfo(server.Find()!, server.ServerArguments, Path.GetTempPath());
        return new LanguageServerRegistration(
            Id: id,
            DisplayName: id,
            Extensions: server.Extensions,
            LanguageId: server.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(
                    info, _loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                _loggerFactory.CreateLogger<VBLspClient>(),
                server.LanguageId));
    }

    private LspClientRegistry RegistryOf(params LanguageServerRegistration[] registrations)
    {
        var registry = new LspClientRegistry(
            registrations, _loggerFactory.CreateLogger<LspClientRegistry>());
        _registries.Add(registry);
        return registry;
    }

    private string Uri(string fileName) =>
        LspDocumentUri.ForFile(Path.Combine(_dir, fileName));

    // ── The second server, on its own ─────────────────────────────────────────────────────────────────

    [ForeignServerFact("latex")]
    public async Task ASecondServerByAnotherAuthorAlsoWorks()
    {
        // Not a duplicate of the Markdown test. That one proved HexIDE can drive a foreign server; this
        // proves it can drive one that shares nothing with the first — different author, different
        // language, different release conventions, different licence.
        var sut = RegistryOf(RegistrationFor(ForeignServer.Latex, "latex"));
        var received = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, p) => received.TrySetResult(p);

        // Deliberately malformed: an environment that is opened and never closed.
        await sut.OpenDocumentAsync(
            Uri("broken.tex"),
            "\\documentclass{article}\n\\begin{document}\n\\begin{itemize}\n\\end{document}\n");

        var published = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
        published.Diagnostics.Should().NotBeEmpty("the document is deliberately malformed LaTeX");
    }

    [ForeignServerFact("latex")]
    public async Task ItAdvertisesItsOwnCapabilitiesRatherThanTheOtherServersHabits()
    {
        // The point of a second server. Anything HexIDE assumed because the first server happened to do it
        // shows up here as a disagreement rather than as a shared blind spot.
        var sut = RegistryOf(RegistrationFor(ForeignServer.Latex, "latex"));

        await sut.OpenDocumentAsync(Uri("a.tex"), "\\documentclass{article}\n");

        var connection = sut.Connections.Single();
        connection.State.Should().Be(LanguageConnectionState.Running);
        connection.LanguageId.Should().Be("latex");
        connection.Capabilities.Should().NotBeNull("a conformant server advertises what it can do");
        ServerCapabilities.AcceptsOpenClose(connection.Capabilities)
            .Should().BeTrue("it accepted didOpen, so it must have advertised document sync");
    }

    // ── Both at once ──────────────────────────────────────────────────────────────────────────────────

    [ForeignServerFact("latex")]
    public async Task EachServerIsOfferedOnlyTheDocumentsItClaims()
    {
        // Two real servers, one registry. Routing has been tested with fakes; this is the same question
        // asked of two processes that will answer for themselves.
        var markdown = ForeignServer.Markdown.Find();
        if (markdown is null) return;   // the [ForeignServerFact] above only guarantees the LaTeX one

        var sut = RegistryOf(
            RegistrationFor(ForeignServer.Markdown, "markdown"),
            RegistrationFor(ForeignServer.Latex, "latex"));

        var byUri = new Dictionary<string, int>();
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, p) =>
        {
            lock (byUri) byUri[p.Uri] = byUri.GetValueOrDefault(p.Uri) + 1;
            settled.TrySetResult();
        };

        var texUri = Uri("paper.tex");
        var mdUri = Uri("README.md");
        await sut.OpenDocumentAsync(texUri, "\\documentclass{article}\n\\begin{itemize}\n");
        await sut.OpenDocumentAsync(mdUri, "##No space\n\n\n");
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(2000);

        // Both connections exist, and each holds only what it claimed. The stronger assertion — that
        // neither was sent the other's document — is what the connection list cannot show, so it is made
        // through the extensions each declared.
        sut.Connections.Should().HaveCount(2);
        sut.Connections.Single(c => c.Id == "latex").Extensions.Should().Contain(".tex");
        sut.Connections.Single(c => c.Id == "markdown").Extensions.Should().Contain(".md");

        lock (byUri)
        {
            byUri.Keys.Should().OnlyContain(
                u => LspDocumentUri.AreSame(u, texUri) || LspDocumentUri.AreSame(u, mdUri),
                "a server should only publish about documents it was actually given");
        }
    }

    // ── The collision ─────────────────────────────────────────────────────────────────────────────────

    [ForeignServerFact("latex")]
    public async Task ALatexServerClaimingClsIsNotOfferedVb6Modules()
    {
        // The rule that stopped the routing fix from becoming a worse bug, now exercised against a real
        // LaTeX server rather than a fake. It genuinely claims `.cls`, and a VB6 module must not reach it:
        // it would parse Visual Basic as LaTeX and report confident nonsense about the developer's source.
        var sut = RegistryOf(RegistrationFor(ForeignServer.Latex, "latex"));

        await sut.OpenDocumentAsync("vb6://module/Module1", "Sub Main()\r\nEnd Sub\r\n");

        // Servers start on the first document of a language they claim, so "was it claimed" is observable
        // as "did it start". The registration is still LISTED — that list is what is configured, not what
        // is running — so the assertion is about its state.
        sut.Connections.Single().State.Should().Be(
            LanguageConnectionState.NotStarted,
            "a VB6 module must not reach a LaTeX server: it would parse Visual Basic as LaTeX and report "
          + "confident nonsense about the developer's own source");
    }

    [ForeignServerFact("latex")]
    public async Task ACarriedLatexClassIsStillOfferedToTheLatexServer()
    {
        // The other half, and the reason the rule is about the SCHEME rather than about `.cls` itself. A
        // real `.cls` on disk routes by extension like any other file, so a server claiming it gets it;
        // what it must not get is the IDE's own modules, which carry no extension at all.
        //
        // Asserted as "the server started", not "it published diagnostics", because measuring this server
        // showed it publishes NOTHING for a `.cls` — it reads class files for definitions rather than
        // linting them. Asserting diagnostics would have meant asserting something untrue about a real
        // server in order to make a point about routing.
        var sut = RegistryOf(RegistrationFor(ForeignServer.Latex, "latex"));

        await sut.OpenDocumentAsync(
            Uri("mystyle.cls"), "\\NeedsTeXFormat{LaTeX2e}\n\\begin{itemize}\n");

        sut.Connections.Single().State.Should().Be(
            LanguageConnectionState.Running, "it claims .cls, so that document is its business");
    }
}
