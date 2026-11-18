using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.LspClient;

// #4484: LspMessageHandler closes a stream it both reads and writes only once its last write ends. A server that
// stops reading while it still writes blocks both peers' writes: the client's because the server isn't reading,
// and, once the client disposes RPC and stops reading, the server's. Neither handler can end them; only the
// transport closing its socket or pipe after DisposeRpc does. Every transport whose handler shares one stream
// must close it on stop, and these cases fail for one that doesn't.
public class BlockedConnectionStopTests
{
    [Theory]
    [InlineData("tcp")]
    [InlineData("pipe")]
    public async Task StopReleasesAConnectionWhoseWritesAreBlockedBothWays(string kind)
    {
        var token = TestContext.Current.CancellationToken;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        var pipeName = $"hexide-{Guid.NewGuid():N}"[..19];
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        ILspTransport transport;
        Task<Stream> accepted;
        if (kind == "tcp")
        {
            listener.Start();
            transport = new TcpLspTransport($"tcp://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}",
                TcpRole.Connect, NullLogger<TcpLspTransport>.Instance);
            accepted = AcceptTcpAsync(listener, token);
        }
        else
        {
            transport = new NamedPipeLspTransport(pipeName, NamedPipeRole.Connect,
                Substitute.For<ILogger<NamedPipeLspTransport>>());
            accepted = AcceptPipeAsync(pipe, token);
        }
        var serving = ServeThenStopReadingAsync(accepted, ready, released, token);

        await using var client = new VBLspClient(transport, NullLogger<VBLspClient>.Instance, "vb6");
        await client.StartAsync(token);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        // The server has stopped reading, so a send blocks once the socket or pipe buffers are full. Windows'
        // loopback TCP buffers tens of megabytes, so keep sending until one doesn't finish.
        const string uri = "file:///C:/blocked/Module1.bas";
        Task? blocked = null;
        for (var version = 1; blocked is null && version <= 128; version++)
        {
            var text = new string('x', 4 * 1024 * 1024) + version;
            var send = version == 1
                ? client.OpenDocumentAsync(uri, text, token)
                : client.ChangeDocumentAsync(uri, version, text, token);
            if (await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(1), token)) != send) blocked = send;
        }
        blocked.Should().NotBeNull("the server stopped reading, so a send blocks once the buffers are full");

        var watch = Stopwatch.StartNew();
        await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(30), token);
        // Shutdown and exit queue behind the blocked write, and each gives up after ShutdownTimeout.
        watch.Elapsed.Should().BeLessThan(VBLspClient.ShutdownTimeout * 2 + TimeSpan.FromSeconds(5));
        await released.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        (await Task.WhenAny(blocked!, Task.Delay(TimeSpan.FromSeconds(10), token))).Should().BeSameAs(blocked,
            "closing the connection ends the client's blocked write as well");
        await serving.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    private static async Task<Stream> AcceptTcpAsync(TcpListener listener, CancellationToken token)
    {
        var server = await listener.AcceptTcpClientAsync(token);
        return server.GetStream();
    }

    private static async Task<Stream> AcceptPipeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        await pipe.WaitForConnectionAsync(token);
        return pipe;
    }

    // Answers initialize, then never reads again and writes until the connection ends.
    private static async Task ServeThenStopReadingAsync(Task<Stream> accepted, TaskCompletionSource ready,
        TaskCompletionSource released, CancellationToken token)
    {
        await using var stream = await accepted;
        while (true)
        {
            using var message = JsonDocument.Parse(await ReadFrameAsync(stream, token));
            var method = message.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null;
            if (method == "initialize")
                await stream.WriteAsync(Frame(
                    """{"jsonrpc":"2.0","id":""" + message.RootElement.GetProperty("id").GetRawText()
                    + ""","result":{"capabilities":{"textDocumentSync":1}}}"""),
                    token);
            else if (method == "initialized")
                break;
        }
        ready.TrySetResult();
        var log = Frame(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", method = "window/logMessage", @params = new { type = 4, message = new string('y', 65536) },
        }));
        try
        {
            while (true) await stream.WriteAsync(log, token);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            released.TrySetResult();
        }
    }

    private static byte[] Frame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
    }

    private static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var header = new StringBuilder();
        var one = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            if (await stream.ReadAsync(one, token) == 0) throw new EndOfStreamException();
            header.Append((char)one[0]);
        }
        var length = int.Parse(header.ToString().Split("\r\n")
            .Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))["Content-Length:".Length..]);
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return body;
    }
}
