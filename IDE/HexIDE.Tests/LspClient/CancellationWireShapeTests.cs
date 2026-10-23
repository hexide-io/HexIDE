using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class CancellationWireShapeTests
{
    [Theory]
    [InlineData(false, "42")]
    [InlineData(true, "\"request-42\"")]
    public async Task StrategySendsNumericAndStringIdsAsScalars(bool stringId, string expectedId)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(clientSide, clientSide,
            new SystemTextJsonFormatter { JsonSerializerOptions = new JsonSerializerOptions(LspJsonContext.Default.Options) }));
        await using var peer = serverSide;
        rpc.StartListening();
        var strategy = new LspCancellationStrategy(rpc, Substitute.For<ILogger<VBLspClient>>());
        strategy.CancelOutboundRequest(stringId ? new RequestId("request-42") : new RequestId(42));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var frame = await ReadFrameAsync(peer, timeout.Token);
        frame.Should().Be("{\"jsonrpc\":\"2.0\",\"method\":\"$/cancelRequest\",\"params\":{\"id\":" + expectedId + "}}");
    }

    [Theory]
    [InlineData(false, "42")]
    [InlineData(true, "\"request-42\"")]
    public async Task IncomingCancellationStillCancelsTheMatchingRequest(bool stringId, string wireId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(clientSide, clientSide,
            new SystemTextJsonFormatter { JsonSerializerOptions = new JsonSerializerOptions(LspJsonContext.Default.Options) }));
        await using var peer = serverSide;
        var strategy = new LspCancellationStrategy(rpc, Substitute.For<ILogger<VBLspClient>>());
        rpc.CancellationStrategy = strategy;
        rpc.AddLocalRpcTarget(strategy);
        rpc.StartListening();
        using var requestCancellation = new CancellationTokenSource();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = requestCancellation.Token.Register(() => cancelled.TrySetResult());
        strategy.IncomingRequestStarted(stringId ? new RequestId("request-42") : new RequestId(42), requestCancellation);
        await WriteFrameAsync(peer,
            "{\"jsonrpc\":\"2.0\",\"method\":\"$/cancelRequest\",\"params\":{\"id\":" + wireId + "}}", timeout.Token);
        await cancelled.Task.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task CancellingAnOutstandingRequestSendsItsBareId()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        await using var log = new ConversationLog();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(),
            "vb6", capture: log, connectionId: "probe");
        // Close the peer before disposing the client even when an assertion or deadline fails.
        await using var serverStream = serverSide;

        // Read real frames rather than binding params through a JSON-RPC handler, which could hide their shape.
        var starting = client.StartAsync(ct);
        using var initialize = JsonDocument.Parse((await ReadFrameAsync(serverSide, ct))!);
        await WriteFrameAsync(serverSide,
            "{\"jsonrpc\":\"2.0\",\"id\":" + initialize.RootElement.GetProperty("id").GetRawText()
            + ",\"result\":{\"capabilities\":{\"completionProvider\":{}}}}", ct);
        await starting;
        using var cancellation = new CancellationTokenSource();
        var pending = client.RequestCompletionAsync("file:///probe.bas", new Position(0, 0), cancellation.Token);
        JsonElement request;
        while (true)
        {
            using var frame = JsonDocument.Parse((await ReadFrameAsync(serverSide, ct))!);
            if (frame.RootElement.GetProperty("method").GetString() != "textDocument/completion") continue;
            request = frame.RootElement.Clone();
            break;
        }
        var id = request.GetProperty("id").GetRawText();
        // This server deliberately never answers completion, including after the cancellation.
        cancellation.Cancel();
        var cancel = await ReadFrameAsync(serverSide, ct);
        cancel.Should().Be("{\"jsonrpc\":\"2.0\",\"method\":\"$/cancelRequest\",\"params\":{\"id\":" + id + "}}");
        await log.DrainAsync();
        var entries = log.Snapshot("probe");
        entries.Should().NotContain(e => e.Method == "$/cancelRequest" && e.Kind == ConversationEntryKind.NeverSent);
        entries.Should().ContainSingle(e => e.Method == "$/cancelRequest" && e.Kind == ConversationEntryKind.Notification);
        pending.IsCompleted.Should().BeFalse("cancellation still waits for the server's response");
        // End the connection to release the abandoned await without answering the request.
        serverSide.Dispose();
        await pending.WaitAsync(ct);
    }

    private static async Task<string?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new List<byte>(64);
        var one = new byte[1];

        while (true)
        {
            if (await stream.ReadAsync(one.AsMemory(0, 1), ct) == 0) return null;
            header.Add(one[0]);
            if (header.Count >= 4
                && header[^4] == (byte)'\r' && header[^3] == (byte)'\n'
                && header[^2] == (byte)'\r' && header[^1] == (byte)'\n')
            {
                break;
            }
        }

        var length = 0;
        foreach (var line in Encoding.ASCII.GetString([.. header]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        // Collapse the interpolated literals' incidental newlines; the header must state the real length.
        var compact = JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement);
        var body = Encoding.UTF8.GetBytes(compact);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
