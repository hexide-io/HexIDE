using HexIDE.Conversations;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What the capture says about the bundled server's handshake, against the real process.
/// </summary>
/// <remarks>
/// <b>The bundled server's advertised set is fully known, and this client uses all of it.</b> So the
/// unconsumed report against it has exactly one right answer, which is nothing. It used to name
/// <c>textDocumentSync</c> on every connection, one entry before the <c>didOpen</c> that capability gates
/// (hexide-io/HexIDE#394), and no test exercised the report against a live handshake, which is the only
/// place it runs.
/// </remarks>
public class BundledServerHandshakeTests : IAsyncDisposable
{
    private VBLspClient? _client;

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            try { await _client.StopAsync(); } catch { /* teardown is best effort */ }
            try { await _client.DisposeAsync(); } catch { /* ditto */ }
        }
        GC.SuppressFinalize(this);
    }

    [BundledServerFact]
    public async Task NothingTheBundledServerAdvertisesIsReportedUnused()
    {
        var capture = new ConversationLog();
        var loggerFactory = LoggerFactory.Create(b => { });
        var transport = new StdioProcessLspTransport(
            new LspServerInfo(BundledServer.Find()!, "", Path.GetTempPath()),
            loggerFactory.CreateLogger<StdioProcessLspTransport>());
        _client = new VBLspClient(
            transport, loggerFactory.CreateLogger<VBLspClient>(), "vb6", capture: capture, connectionId: "vb6");

        await _client.StartAsync(TestContext.Current.CancellationToken);

        // Prove the handshake landed before reading anything into an empty report: a server that never
        // answered initialize produces no unconsumed entries either.
        var entries = capture.Snapshot("vb6");
        entries.Should().Contain(
            e => e.Kind == ConversationEntryKind.Lifecycle && e.Detail != null
                 && e.Detail.StartsWith("initialized:", StringComparison.Ordinal),
            "the report is written when the handshake lands, so without one there is nothing to judge");

        entries.Where(e => e.Kind == ConversationEntryKind.Unconsumed).Select(e => e.Detail)
            .Should().BeEmpty("this client uses everything the bundled server advertises");
    }
}
