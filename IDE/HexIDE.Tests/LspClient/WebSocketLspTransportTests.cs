using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using StreamJsonRpc.Protocol;
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

    [Fact]
    public async Task ListenRole_AcceptsIncomingSocketAndWritesBareJsonFrame()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var endpoint = $"ws://localhost:{((IPEndPoint)port.LocalEndpoint).Port}/lsp/";
        port.Stop();
        await using var sut = new WebSocketLspTransport(endpoint, _logger, WebSocketRole.Listen);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        var connecting = sut.ConnectAsync(new SystemTextJsonFormatter(), cts.Token);
        using var peer = new ClientWebSocket();
        await peer.ConnectAsync(new Uri(endpoint), cts.Token);
        var handler = await connecting;
        handler.Should().NotBeNull();
        using var rpc = new JsonRpc(handler!);
        sut.IsAlive.Should().BeTrue();
        await handler!.WriteAsync(new JsonRpcRequest { RequestId = new RequestId(1), Method = "hexide/ping" }, cts.Token);
        var buffer = new byte[4096];
        var frame = await peer.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
        frame.MessageType.Should().Be(WebSocketMessageType.Text);
        frame.EndOfMessage.Should().BeTrue();
        Encoding.UTF8.GetString(buffer, 0, frame.Count).Should().Contain("hexide/ping").And.NotContain("Content-Length");
        await peer.SendAsync(Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"result":"pong"}"""),
            WebSocketMessageType.Text, true, cts.Token);
        var reply = (await handler.ReadAsync(cts.Token)).Should().BeAssignableTo<JsonRpcResult>().Subject;
        reply.RequestId.Should().Be(new RequestId(1));
        peer.Abort();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListenRole_CancellationOrDisposalReleasesTheListeningEndpoint(bool cancel)
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var endpoint = $"ws://localhost:{((IPEndPoint)port.LocalEndpoint).Port}/lsp/";
        port.Stop();
        await using var sut = new WebSocketLspTransport(endpoint, _logger, WebSocketRole.Listen);
        using var cts = new CancellationTokenSource();
        var connecting = sut.ConnectAsync(new SystemTextJsonFormatter(), cts.Token);
        using var competing = new HttpListener();
        competing.Prefixes.Add(endpoint.Replace("ws://", "http://", StringComparison.Ordinal));
        Action bind = competing.Start;
        bind.Should().Throw<HttpListenerException>("the IDE owns the endpoint while waiting");
        if (cancel) await cts.CancelAsync();
        else await sut.DisposeAsync();
        (await connecting).Should().BeNull();
        sut.IsAlive.Should().BeFalse();
        using var replacement = new HttpListener();
        replacement.Prefixes.Add(endpoint.Replace("ws://", "http://", StringComparison.Ordinal));
        replacement.Start();
        replacement.IsListening.Should().BeTrue();
    }

    // Whatever dials in becomes the language server, which can edit the document, so the IDE listens on this
    // machine only (#384).
    [Theory]
    [InlineData("ws://0.0.0.0:{0}/lsp/")]
    [InlineData("ws://example.invalid:{0}/lsp/")]
    public async Task ListenRole_RefusesAnEndpointOffThisMachine(string template)
    {
        await using var sut = new WebSocketLspTransport(string.Format(template, FreePort()), _logger, WebSocketRole.Listen);
        var handler = await sut.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        handler.Should().BeNull();
        sut.LastFailure.Should().Contain("must be on this machine");
    }

    // A browser sends Origin on every WebSocket handshake and is not bound by the same-origin policy for one, so
    // any page open in it could otherwise dial a listening IDE first and be taken as its language server.
    [Fact]
    public async Task ListenRole_RefusesAWebPageAndStillAcceptsTheServerThatDialsAfterIt()
    {
        var endpoint = $"ws://localhost:{FreePort()}/lsp/";
        await using var sut = new WebSocketLspTransport(endpoint, _logger, WebSocketRole.Listen);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        var connecting = sut.ConnectAsync(new SystemTextJsonFormatter(), cts.Token);

        using var page = new ClientWebSocket();
        page.Options.SetRequestHeader("Origin", "http://attacker.example");
        Func<Task> fromPage = () => page.ConnectAsync(new Uri(endpoint), cts.Token);
        await fromPage.Should().ThrowAsync<WebSocketException>();

        using var server = new ClientWebSocket();
        await server.ConnectAsync(new Uri(endpoint), cts.Token);
        (await connecting).Should().NotBeNull();
        sut.IsAlive.Should().BeTrue();
        server.Abort();
    }

    private static int FreePort()
    {
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        return ((IPEndPoint)port.LocalEndpoint).Port;
    }
}
