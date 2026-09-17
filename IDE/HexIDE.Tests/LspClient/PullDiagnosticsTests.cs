using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using HexIDE.Conversations;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Drives a server that will not say a word unless it is asked.
///
/// <para>
/// Every other foreign server here publishes diagnostics unbidden, which is how HexIDE got four servers
/// deep without anyone noticing that it never asks (hexide-io/HexIDE#284). A pull-model server makes that
/// failure loud rather than subtle: a file with an obvious problem produces no marks at all.
/// </para>
/// </summary>
public class PullDiagnosticsTests : IAsyncDisposable
{
    /// <summary>
    /// An unused import — <c>F401</c>, which is on by default in ruff's rule set. A specific code rather
    /// than "something non-empty": a test that only asserts non-empty passes for a server that reported an
    /// unrelated problem, which is the fail-open shape this suite keeps finding.
    /// </summary>
    private const string PythonWithAnUnusedImport = "import os\n\n\ndef greet(name):\n    return name\n";

    private LspClientRegistry? _registry;
    private string? _directory;
    private readonly ConversationLog _capture = new();

    /// <summary>
    /// Writes the document to disk and answers its URI.
    /// </summary>
    /// <remarks>
    /// A real file, unlike the Markdown tests' invented path. This server resolves its configuration by
    /// walking up from the file it is asked about, so a path under a directory that exists is one fewer
    /// difference between the test and the way anybody actually uses it.
    /// </remarks>
    private string PythonDocument()
    {
        _directory = Path.Combine(Path.GetTempPath(), "hexide-py-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "sample.py");
        File.WriteAllText(file, PythonWithAnUnusedImport);
        return new Uri(file).AbsoluteUri;
    }

    private LspClientRegistry PythonRegistry()
    {
        var serverInfo = new LspServerInfo(
            ForeignServer.Python.Find()!, ForeignServer.Python.ServerArguments, _directory ?? Path.GetTempPath());

        var loggerFactory = LoggerFactory.Create(b => { });
        var registration = new LanguageServerRegistration(
            Id: "foreign.python",
            DisplayName: "Foreign Python server",
            Extensions: ForeignServer.Python.Extensions,
            LanguageId: ForeignServer.Python.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(serverInfo, loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                loggerFactory.CreateLogger<VBLspClient>(),
                ForeignServer.Python.LanguageId,
                capture: _capture, connectionId: "foreign.python"));

        _registry = new LspClientRegistry([registration], loggerFactory.CreateLogger<LspClientRegistry>());
        return _registry;
    }

    [ForeignServerFact("python")]
    public async Task ItAdvertisesThePullModelInTheShapeThatOnceBrokeTheHandshake()
    {
        // `diagnosticProvider` is `boolean | DiagnosticOptions | DiagnosticRegistrationOptions`, and this
        // server answers in the OBJECT form. That is the shape #238 was about: a capability modelled as
        // bool? threw during initialize, the throw landed in a swallowing catch, and every language feature
        // went dark with nothing a user could see. Asserted against a real server rather than trusted.
        var sut = PythonRegistry();
        var uri = PythonDocument();

        await sut.OpenDocumentAsync(uri, PythonWithAnUnusedImport);

        var connection = sut.Connections.Single();
        connection.State.Should().Be(LanguageConnectionState.Running,
            "{0}", ConnectionDiagnostics.Explain(connection, _capture));

        connection.Capabilities.Should().NotBeNull();
        var capabilities = connection.Capabilities!.Value;
        capabilities.TryGetProperty("diagnosticProvider", out var provider).Should().BeTrue(
            "this server delivers diagnostics only by request, so it must advertise that it does");
        provider.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object,
            "it answers in the options form rather than a bare `true` — the half of the contract #238 "
          + "rejected");
    }

    [ForeignServerFact("python")]
    public async Task DeclaringThePullModelStopsTheServerPublishing()
    {
        // The proof that the negotiation actually happened, and the reason both halves had to ship at once.
        //
        // This server decides how to deliver diagnostics from what the client declared — measured, by
        // running it both ways. Told nothing, it publishes. Told the client can ask, it publishes NOTHING
        // and waits. So declaring the capability without issuing the request is strictly worse than
        // declaring neither: it talks a working server into silence. That is #267's lesson with a server
        // that enforces it rather than one that forgives it.
        //
        // Asserted on the WIRE rather than on the diagnostics channel, and that distinction is the point:
        // the pull path raises on that same channel, so a channel-level assertion cannot tell "it
        // published" from "we asked and it answered". The capture can, because only one is a notification.
        var sut = PythonRegistry();
        var uri = PythonDocument();
        var received = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, p) => { if (p.Diagnostics.Length > 0) received.TrySetResult(p); };

        await sut.OpenDocumentAsync(uri, PythonWithAnUnusedImport);
        var published = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await _capture.DrainAsync();

        var wire = _capture.Snapshot("foreign.python");
        wire.Should().Contain(e => e.Method == "textDocument/diagnostic",
            "the client must have asked — nothing else would have produced these");
        wire.Should().NotContain(e => e.Method == "textDocument/publishDiagnostics",
            "and the server must have stopped publishing, because it was told it need not");

        // Asserted on WHAT was reported, never merely that something was. A non-empty assertion passes for
        // an unrelated diagnostic, which is the fail-open shape this suite keeps finding in itself.
        //
        // By CODE now, which is the assertion this test wanted all along and could not make until
        // `Diagnostic` carried one (hexide-io/HexIDE#426). A rule identifier is stable where the prose
        // beside it is not: the message is version-fragile and localizable, `F401` is neither.
        published.Diagnostics.Should().Contain(
            d => d.Source == "Ruff" && d.CodeText == "F401",
            "the document's only defect is an unused import, and that is the rule that names it");
        published.Diagnostics.Should().Contain(
            d => d.CodeDescription != null && d.CodeDescription.Href!.Contains("unused-import", StringComparison.Ordinal),
            "this server documents each rule, and the link is the other half of what a code is for");
        published.Diagnostics.Should().OnlyContain(
            d => d.Range.Start.Line >= 0 && d.Range.End.Line >= d.Range.Start.Line,
            "ranges must be well-formed, or the editor cannot place a marker");
    }

    public async ValueTask DisposeAsync()
    {
        if (_registry is not null)
        {
            try { await _registry.DisposeAsync(); } catch { /* teardown is best effort */ }
        }

        await _capture.DisposeAsync();

        if (_directory is not null)
        {
            try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }
}
