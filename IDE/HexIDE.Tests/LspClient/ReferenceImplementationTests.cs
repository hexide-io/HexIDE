using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// HexIDE driving the reference implementation.
///
/// <para>
/// The other foreign servers establish independence — different authors, different frameworks, different
/// habits. This one establishes something else: <c>vscode-languageserver-node</c> is the library the
/// specification is written around, and where the prose is ambiguous, what that library does is what
/// server authors treat as correct. Disagreeing with it is a defect here regardless of what the wording
/// permits.
/// </para>
///
/// <para>
/// It is the only server in the suite that costs a runtime dependency, which is why it is the only one
/// that had to be argued for. Its published package will not run from its own tarball — verified — so it
/// is installed with <c>npm ci</c> against a committed lockfile, which pins every one of its thirty-odd
/// packages by an integrity hash the registry attests.
/// </para>
/// </summary>
public class ReferenceImplementationTests : IAsyncDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-refimpl-" + Guid.NewGuid().ToString("N"));

    private readonly List<LspClientRegistry> _registries = [];
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => { });

    public ReferenceImplementationTests() => Directory.CreateDirectory(_dir);

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

    private LspClientRegistry ReferenceRegistry()
    {
        var server = ForeignServer.Json;
        var info = new LspServerInfo(server.Find()!, server.LaunchArguments, Path.GetTempPath());

        var registration = new LanguageServerRegistration(
            Id: "reference.json",
            DisplayName: "Reference JSON server",
            Extensions: server.Extensions,
            LanguageId: server.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(
                    info, _loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                _loggerFactory.CreateLogger<VBLspClient>(),
                server.LanguageId));

        var registry = new LspClientRegistry(
            [registration], _loggerFactory.CreateLogger<LspClientRegistry>());
        _registries.Add(registry);
        return registry;
    }

    private string Uri(string fileName) => LspDocumentUri.ForFile(Path.Combine(_dir, fileName));

    [ForeignServerFact("json")]
    public async Task ItAcceptsOurHandshakeAndAnswersAboutADocument()
    {
        // The whole point in one test. If HexIDE's `initialize` payload, its document synchronization or
        // its URI handling diverged from what the specification's own library expects, this is where it
        // shows — and it would show as a defect here whatever a permissive reading of the prose allowed.
        var sut = ReferenceRegistry();
        var received = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        sut.DiagnosticsPublished += (_, p) => received.TrySetResult(p);

        // Deliberately invalid JSON: a trailing comma and an unclosed brace.
        await sut.OpenDocumentAsync(Uri("settings.json"), "{ \"a\": 1, }");

        var published = await received.Task.WaitAsync(TimeSpan.FromSeconds(60));

        published.Diagnostics.Should().NotBeEmpty("the document is deliberately invalid JSON");
        LspDocumentUri.AreSame(published.Uri, Uri("settings.json")).Should().BeTrue(
            "the reference implementation normalises URIs its own way, and #236 is why this is checked "
          + "rather than assumed");
    }

    [ForeignServerFact("json")]
    public async Task ItAdvertisesCapabilitiesWeCanRead()
    {
        // Capability shapes are where this client has been wrong twice (#238, and the textDocumentSync
        // reader). The reference implementation is the one whose shapes are by definition the intended
        // ones, so anything our readers cannot make sense of here is our bug.
        var sut = ReferenceRegistry();

        await sut.OpenDocumentAsync(Uri("a.json"), "{}");

        var connection = sut.Connections.Single();
        connection.State.Should().Be(LanguageConnectionState.Running);
        connection.Capabilities.Should().NotBeNull();
        ServerCapabilities.AcceptsOpenClose(connection.Capabilities).Should().BeTrue(
            "it accepted didOpen, so its advertised sync must read as accepting open and close");
        ServerCapabilities.AcceptsChanges(connection.Capabilities).Should().BeTrue();
    }

    [ForeignServerFact("json")]
    public async Task EditingADocumentUpdatesWhatItReports()
    {
        // Full-document synchronization, against the library the sync model is specified around. A
        // divergence in how changes are framed would show as diagnostics that never clear.
        var sut = ReferenceRegistry();
        var latest = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = 0;
        sut.DiagnosticsPublished += (_, p) =>
        {
            if (Interlocked.Increment(ref seen) > 1) latest.TrySetResult(p);
        };

        var uri = Uri("settings.json");
        await sut.OpenDocumentAsync(uri, "{ \"a\": 1, }");
        await Task.Delay(1500);

        await sut.ChangeDocumentAsync(uri, 2, "{ \"a\": 1 }");   // now valid

        var published = await latest.Task.WaitAsync(TimeSpan.FromSeconds(60));
        published.Diagnostics.Should().BeEmpty(
            "the document was corrected, and an empty set is how a server says the problems are resolved");
    }
}
