using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// Phase 2 unit tests for the WebSocket transport's transport-level contract. End-to-end behaviour
/// (real LSP over the stdio↔WS bridge, plus reconnect/resync) is verified live against the bridge.
/// </summary>
public class WebSocketLspTransportTests
{
    private readonly ILogger<WebSocketLspTransport> _logger = Substitute.For<ILogger<WebSocketLspTransport>>();

    [Fact]
    public void CanReconnect_IsTrue_AndNotAliveBeforeConnect()
    {
        var sut = new WebSocketLspTransport("ws://127.0.0.1:1/", _logger);

        sut.CanReconnect.Should().BeTrue();
        sut.IsAlive.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectAsync_WhenEndpointUnreachable_ReturnsNullGracefully()
    {
        var sut = new WebSocketLspTransport("ws://127.0.0.1:1/", _logger);
        var formatter = new SystemTextJsonFormatter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var handler = await sut.ConnectAsync(formatter, cts.Token);

        handler.Should().BeNull();
        sut.IsAlive.Should().BeFalse();
        await sut.DisposeAsync();
    }
}
