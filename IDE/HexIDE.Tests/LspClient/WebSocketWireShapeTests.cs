using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

namespace HexIDE.Tests.LspClient;

/// <summary>
/// What actually crosses a WebSocket, read as frames rather than trusted because a call returned.
/// </summary>
/// <remarks>
/// <b>The framing was correct and completely unproven (hexide-io/HexIDE#382).</b> Both existing WebSocket
/// tests dial <c>ws://127.0.0.1:1/</c>, where nothing listens: <c>ConnectAsync</c> returns null, no message
/// handler is ever constructed, and no byte is ever framed. The only end-to-end check this path ever had
/// was manual, against the debug proxy's own bridge — which #369 phase 5 removes, so the assertion had to
/// land first or the retirement would convert an unproven-but-checkable path into an unproven one with
/// nothing to check it against.
///
/// <para>
/// <b>Two ways it goes wrong silently, and both are what these tests are aimed at.</b> Somebody reuses
/// <c>HeaderDelimitedMessageHandler</c> on the socket path — every existing test still passes, and the
/// bytes gain a <c>Content-Length</c> header that a JavaScript bridge does not expect. Or a StreamJsonRpc
/// bump changes <c>WebSocketMessageHandler</c>'s frame type from Text to Binary, which several bridges
/// reject outright. This project already pins <c>xunit.v3</c> below 4.0.0 because a routine bump broke
/// discovery with no build error, so that is not a hypothetical.
/// </para>
///
/// <para>
/// The shape is <c>ShutdownWireShapeTests</c>': read what crossed, assert on it. The counterparty is a
/// listener socket doing the WebSocket handshake by hand, so the frames are observed by the operating
/// system's own framing rather than by anything HexIDE wrote — and so the test needs no URL reservation,
/// which <c>HttpListener</c> would on Windows.
/// </para>
/// </remarks>
public class WebSocketWireShapeTests
{
    private readonly ILogger<WebSocketLspTransport> _logger = Substitute.For<ILogger<WebSocketLspTransport>>();

    [Fact]
    public async Task OneJsonRpcMessageCrossesAsOneTextFrameCarryingBareJson()
    {
        // The whole contract in one assertion set: message-per-frame, Text, and no length header. A
        // JavaScript bridge in front of a stdio server rejects any of the three being wrong.
        await using var host = await WireHost.StartAsync();

        var sut = new WebSocketLspTransport(host.Endpoint, _logger);
        var handler = await sut.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);
        handler.Should().NotBeNull("the listener is up, so the transport must connect");

        using (var rpc = new JsonRpc(handler!))
        {
            rpc.StartListening();
            var answer = await rpc.InvokeWithCancellationAsync<string>(
                "hexide/ping", null, TestContext.Current.CancellationToken);
            answer.Should().Be("pong", "the round trip must complete, or the framing proved nothing");
        }

        await sut.DisposeAsync();

        var request = host.Received.Should().ContainSingle().Subject;

        request.Type.Should().Be(WebSocketMessageType.Text,
            "several JavaScript bridges reject a binary frame outright");
        request.EndOfMessage.Should().BeTrue(
            "one JSON-RPC message is one WebSocket message, not a fragment of one");

        request.Text.Should().NotContain("Content-Length",
            "the socket carries message boundaries itself; a length header means the byte-stream handler "
          + "was used on the socket path");
        request.Text.TrimStart().Should().StartWith("{",
            "the payload is a bare JSON-RPC message, with no header block in front of it");
    }

    [Fact]
    public async Task TheFramePayloadIsTheJsonRpcMessageItself()
    {
        // Reading the bytes is only worth doing if the assertion is about their content. A frame that is
        // Text and unheadered but carries something other than the message would satisfy the test above.
        await using var host = await WireHost.StartAsync();

        var sut = new WebSocketLspTransport(host.Endpoint, _logger);
        var handler = await sut.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        using (var rpc = new JsonRpc(handler!))
        {
            rpc.StartListening();
            await rpc.InvokeWithCancellationAsync<string>(
                "hexide/ping", null, TestContext.Current.CancellationToken);
        }

        await sut.DisposeAsync();

        using var parsed = JsonDocument.Parse(host.Received.Single().Text);
        var root = parsed.RootElement;

        root.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        root.GetProperty("method").GetString().Should().Be("hexide/ping");
        root.TryGetProperty("id", out _).Should().BeTrue("a request carries the id its answer will quote");
    }

    [Fact]
    public async Task EachMessageGetsItsOwnFrame()
    {
        // The failure this catches is a handler that concatenates or splits. Three requests must produce
        // exactly three complete messages, in order.
        await using var host = await WireHost.StartAsync();

        var sut = new WebSocketLspTransport(host.Endpoint, _logger);
        var handler = await sut.ConnectAsync(new SystemTextJsonFormatter(), TestContext.Current.CancellationToken);

        using (var rpc = new JsonRpc(handler!))
        {
            rpc.StartListening();
            await rpc.InvokeWithCancellationAsync<string>(
                "hexide/ping", null, TestContext.Current.CancellationToken);
            await rpc.InvokeWithCancellationAsync<string>(
                "hexide/ping", null, TestContext.Current.CancellationToken);
            await rpc.InvokeWithCancellationAsync<string>(
                "hexide/ping", null, TestContext.Current.CancellationToken);
        }

        await sut.DisposeAsync();

        host.Received.Should().HaveCount(3);
        host.Received.Should().AllSatisfy(f =>
        {
            f.EndOfMessage.Should().BeTrue();
            f.Type.Should().Be(WebSocketMessageType.Text);
        });
    }

    [Fact]
    public async Task TheOtherHandlerWouldBeVisiblyDifferent()
    {
        // PROOF THAT THE ASSERTION ABOVE HAS TEETH. "No Content-Length in the payload" only means something
        // if the regression it guards against would put one there. The two byte-stream transports use
        // HeaderDelimitedMessageHandler, and reusing it on the socket path is the first of the two silent
        // failures #382 names — so write the same message through it and look.
        //
        // Without this, the socket test would pass just as happily against a discriminator that could never
        // fire, which is the failure mode this project keeps paying for.
        // Read from the far end of a pair rather than inspecting a MemoryStream this test wrote into.
        // WriteAsync hands the message to a PipeWriter and returns before the drain onto the stream has
        // happened, and disposing does not reliably wait for it either — measured: against a MemoryStream
        // this assertion saw an empty buffer roughly two runs in five. A read completes when the bytes
        // arrive, which is the only version of this that is not a race.
        var (mine, theirs) = FullDuplexStream.CreatePair();
        var handler = new HeaderDelimitedMessageHandler(mine, mine, new SystemTextJsonFormatter());

        await handler.WriteAsync(
            new JsonRpcRequest { RequestId = new RequestId(1), Method = "hexide/ping" },
            TestContext.Current.CancellationToken);

        var written = await ReadUntilAsync(theirs, "hexide/ping");

        written.Should().StartWith("Content-Length:",
            "this is the shape the socket path must NOT produce; if it ever stopped producing one here, "
          + "the socket assertion would be guarding against nothing");
        written.Should().Contain("hexide/ping", "and it is the same message, framed differently");
    }

    /// <summary>
    /// Reads until <paramref name="marker"/> is in hand, so the assertion runs on a complete frame.
    /// </summary>
    /// <remarks>
    /// A single read returns what has arrived, not what will: the header and the body can land in separate
    /// reads, and stopping at the first would make the test's answer depend on scheduling.
    /// </remarks>
    private static async Task<string> ReadUntilAsync(Stream stream, string marker)
    {
        var buffer = new byte[4096];
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(total), TestContext.Current.CancellationToken);
            if (read == 0) break;

            total += read;
            if (Encoding.UTF8.GetString(buffer, 0, total).Contains(marker, StringComparison.Ordinal))
                break;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    /// <summary>One WebSocket message as the counterparty saw it.</summary>
    private sealed record Frame(WebSocketMessageType Type, bool EndOfMessage, string Text);

    /// <summary>
    /// A WebSocket counterparty that records what arrives and answers it.
    /// </summary>
    /// <remarks>
    /// A bare <see cref="TcpListener"/> plus the handshake written out, rather than
    /// <see cref="HttpListener"/>: the latter needs a URL reservation on Windows for anything but an
    /// elevated process, which would make this test pass locally and fail on somebody else's machine.
    /// <c>WebSocket.CreateFromStream</c> then does the framing, so what is asserted is the operating
    /// system's view of the bytes and not anything HexIDE wrote.
    /// </remarks>
    private sealed class WireHost : IAsyncDisposable
    {
        private const string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly List<Frame> _received = [];
        private readonly Task _serving;

        private WireHost(TcpListener listener, int port)
        {
            _listener = listener;
            Endpoint = $"ws://127.0.0.1:{port}/";
            _serving = Task.Run(ServeAsync);
        }

        public string Endpoint { get; }

        /// <summary>Every message that arrived, in order. Read after the socket has closed.</summary>
        public IReadOnlyList<Frame> Received
        {
            get { lock (_received) return [.. _received]; }
        }

        public static Task<WireHost> StartAsync()
        {
            // Port 0 asks the operating system for a free one, so parallel runs and a developer's own
            // occupied ports cannot collide.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            return Task.FromResult(new WireHost(listener, port));
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                await using var stream = client.GetStream();

                var key = await ReadHandshakeAsync(stream);
                if (key is null) return;

                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + Magic)));
                var response =
                    "HTTP/1.1 101 Switching Protocols\r\n"
                    + "Upgrade: websocket\r\n"
                    + "Connection: Upgrade\r\n"
                    + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stopping.Token);
                await stream.FlushAsync(_stopping.Token);

                using var socket = WebSocket.CreateFromStream(
                    stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);

                await PumpAsync(socket);
            }
            catch (OperationCanceledException) { /* stopping */ }
            catch (Exception) { /* the client went away; the assertions describe what arrived */ }
        }

        /// <summary>Reads the upgrade request and returns its Sec-WebSocket-Key.</summary>
        private async Task<string?> ReadHandshakeAsync(NetworkStream stream)
        {
            var request = new StringBuilder();
            var one = new byte[1];

            while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await stream.ReadAsync(one, _stopping.Token);
                if (read == 0) return null;
                request.Append((char)one[0]);

                // A client that is not speaking HTTP would otherwise loop until the test times out.
                if (request.Length > 8192) return null;
            }

            foreach (var line in request.ToString().Split("\r\n"))
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                    return line["Sec-WebSocket-Key:".Length..].Trim();
            }

            return null;
        }

        /// <summary>Records each message and answers a request with its id, so the round trip completes.</summary>
        private async Task PumpAsync(WebSocket socket)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
                while (socket.State == WebSocketState.Open && !_stopping.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stopping.Token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(message.ToArray());
                    lock (_received) _received.Add(new Frame(result.MessageType, result.EndOfMessage, text));

                    if (Answer(text) is { } reply)
                    {
                        await socket.SendAsync(
                            Encoding.UTF8.GetBytes(reply), WebSocketMessageType.Text,
                            endOfMessage: true, _stopping.Token);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        /// <summary>A minimal JSON-RPC answer, or null for a notification.</summary>
        private static string? Answer(string request)
        {
            try
            {
                using var parsed = JsonDocument.Parse(request);
                if (!parsed.RootElement.TryGetProperty("id", out var id)) return null;

                var raw = id.ValueKind == JsonValueKind.String ? $"\"{id.GetString()}\"" : id.GetRawText();
                return $$"""{"jsonrpc":"2.0","id":{{raw}},"result":"pong"}""";
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync();
            _listener.Stop();

            try { await _serving.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { /* the assertions have what they need */ }

            _stopping.Dispose();
        }
    }
}
