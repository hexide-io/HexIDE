using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — `HexIDE.Tests.Lsp` would shadow the
// real `HexIDE.Lsp` for sibling tests that reference `Lsp.X` unqualified.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Phase 1 (transport seam) contract tests: VBLspClient must be fail-soft when the transport
/// reports no server, and the InjectDiagnosticsAsync side channel must work regardless of the
/// server/transport state. These pin the behaviour the Phase 1 refactor must preserve.
/// </summary>
public class VBLspClientTests
{
    private readonly ILspTransport _transport = Substitute.For<ILspTransport>();
    private readonly ILogger<VBLspClient> _logger = Substitute.For<ILogger<VBLspClient>>();

    private VBLspClient CreateSut() => new(_transport, _logger);

    private void GivenTransportHasNoServer() =>
        _transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IJsonRpcMessageHandler?>(null));

    [Fact]
    public async Task StartAsync_WhenTransportHasNoServer_StaysDisabledAndDoesNotThrow()
    {
        GivenTransportHasNoServer();
        var sut = CreateSut();

        var start = async () => await sut.StartAsync();

        await start.Should().NotThrowAsync();
        sut.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Requests_WhenNotInitialized_ReturnEmptyOrNullDefaults()
    {
        GivenTransportHasNoServer();
        var sut = CreateSut();
        await sut.StartAsync();

        const string uri = "vb6://m/Module1";
        var pos = new Position(0, 0);

        // Notifications must be safe no-ops.
        await sut.OpenDocumentAsync(uri, "Sub Foo()\nEnd Sub");
        await sut.ChangeDocumentAsync(uri, 2, "Sub Foo()\nEnd Sub");
        await sut.CloseDocumentAsync(uri);

        // Collection requests return empty; single-result requests return null.
        (await sut.RequestHoverAsync(uri, pos)).Should().BeNull();
        (await sut.RequestDocumentSymbolsAsync(uri)).Should().BeEmpty();
        (await sut.RequestFoldingRangesAsync(uri)).Should().BeEmpty();
        (await sut.RequestCompletionAsync(uri, pos)).Should().BeEmpty();
        (await sut.RequestSignatureHelpAsync(uri, pos)).Should().BeNull();
        (await sut.RequestDefinitionAsync(uri, pos)).Should().BeNull();
        (await sut.RequestDocumentHighlightAsync(uri, pos)).Should().BeNull();
        (await sut.RequestRenameAsync(uri, pos, "NewName")).Should().BeNull();
        (await sut.RequestFormattingAsync(uri)).Should().BeEmpty();
    }

    [Fact]
    public async Task InjectDiagnosticsAsync_RaisesDiagnosticsPublished_WithoutAServer()
    {
        var sut = CreateSut();
        PublishDiagnosticsParams? captured = null;
        sut.DiagnosticsPublished += (_, p) => captured = p;

        var diagnostic = new Diagnostic(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 4)),
            "Expected end of statement",
            DiagnosticSeverity.Error);
        await sut.InjectDiagnosticsAsync("vb6://m/Module1", [diagnostic]);

        captured.Should().NotBeNull();
        captured!.Uri.Should().Be("vb6://m/Module1");
        captured.Diagnostics.Should().ContainSingle()
            .Which.Message.Should().Be("Expected end of statement");
    }

    [Fact]
    public async Task InjectDiagnosticsAsync_WithEmptyArray_ClearsDiagnostics()
    {
        var sut = CreateSut();
        PublishDiagnosticsParams? captured = null;
        sut.DiagnosticsPublished += (_, p) => captured = p;

        await sut.InjectDiagnosticsAsync("vb6://m/Module1", []);

        captured.Should().NotBeNull();
        captured!.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task StopAsync_IsSafeWhenNeverConnected_AndIdempotent()
    {
        GivenTransportHasNoServer();
        var sut = CreateSut();
        await sut.StartAsync();

        var stopTwice = async () =>
        {
            await sut.StopAsync();
            await sut.StopAsync();
        };

        await stopTwice.Should().NotThrowAsync();
        sut.IsRunning.Should().BeFalse();
    }
}
