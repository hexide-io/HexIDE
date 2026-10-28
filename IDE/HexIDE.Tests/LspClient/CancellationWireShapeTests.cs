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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingAnOutstandingRequestSendsItsBareId(bool deadline)
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
        var logger = Substitute.For<ILogger<VBLspClient>>();
        await using var client = new VBLspClient(transport, logger,
            "vb6", capture: log, connectionId: "probe");
        client.RequestTimeout = deadline ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(30);
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
        var answers = 0;
        var pending = ((ILspClient)client).RequestCompletionAsync("file:///probe.bas", new Position(0, 0),
            cancellation.Token, () => answers++);
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
        if (!deadline) cancellation.Cancel();
        var cancel = await ReadFrameAsync(serverSide, ct);
        cancel.Should().Be("{\"jsonrpc\":\"2.0\",\"method\":\"$/cancelRequest\",\"params\":{\"id\":" + id + "}}");
        (await pending.WaitAsync(ct)).Should().BeEmpty();
        answers.Should().Be(0, "cancellation and timeout are not server answers");
        await log.DrainAsync();
        var entries = log.Snapshot("probe");
        entries.Should().NotContain(e => e.Method == "$/cancelRequest" && e.Kind == ConversationEntryKind.NeverSent);
        entries.Should().ContainSingle(e => e.Method == "$/cancelRequest" && e.Kind == ConversationEntryKind.Notification);
        entries.Should().ContainSingle(e => e.Method == "textDocument/completion" && e.Kind == ConversationEntryKind.Request)
            .Which.Outcome.Should().Be(deadline ? ConversationOutcome.TimedOut : ConversationOutcome.Cancelled);
        if (deadline)
        {
            var repeated = client.RequestCompletionAsync("file:///probe.bas", new Position(0, 0), ct);
            using var repeatedFrame = JsonDocument.Parse((await ReadFrameAsync(serverSide, ct))!);
            using var repeatedCancel = JsonDocument.Parse((await ReadFrameAsync(serverSide, ct))!);
            repeatedCancel.RootElement.GetProperty("params").GetProperty("id").GetRawText().Should()
                .Be(repeatedFrame.RootElement.GetProperty("id").GetRawText());
            (await repeated.WaitAsync(ct)).Should().BeEmpty();
        }
        logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "Log"
            && call.GetArguments()[0] is LogLevel.Warning).Should().Be(deadline ? 1 : 0);
        var second = ((ILspClient)client).RequestCompletionAsync("file:///second.bas", new Position(0, 0),
            ct, () => answers++);
        using var secondFrame = JsonDocument.Parse((await ReadFrameAsync(serverSide, ct))!);
        await WriteFrameAsync(serverSide, "{\"jsonrpc\":\"2.0\",\"id\":"
            + secondFrame.RootElement.GetProperty("id").GetRawText() + ",\"result\":{\"items\":[]}}", ct);
        (await second.WaitAsync(ct)).Should().BeEmpty();
        answers.Should().Be(1, "a valid empty reply is still an answer");
        await log.DrainAsync();
        log.Snapshot("probe").Should().ContainSingle(e => e.Kind == ConversationEntryKind.Request
            && e.CorrelationId == secondFrame.RootElement.GetProperty("id").GetRawText())
            .Which.Outcome.Should().Be(ConversationOutcome.Answered);
        client.IsRunning.Should().BeTrue();
        // End the connection to release StreamJsonRpc's abandoned pending entries.
        serverSide.Dispose();
        await pending.WaitAsync(ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangeCancelsOnlySupersededDocumentRequestsBeforeNotification(bool semantic)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        var logger = Substitute.For<ILogger<VBLspClient>>();
        await using var client = new VBLspClient(transport, logger, "vb6");
        await using var peer = serverSide;
        var starting = client.StartAsync(ct);
        using var initialize = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        await WriteFrameAsync(peer, "{\"jsonrpc\":\"2.0\",\"id\":"
            + initialize.RootElement.GetProperty("id").GetRawText()
            + ",\"result\":{\"capabilities\":{\"textDocumentSync\":1,\"diagnosticProvider\":{},\"foldingRangeProvider\":true,\"semanticTokensProvider\":{\"full\":true,\"legend\":{\"tokenTypes\":[\"class\"],\"tokenModifiers\":[]}},\"hoverProvider\":true,\"completionProvider\":{}}}}", ct);
        await starting;
        await ReadFrameAsync(peer, ct); // initialized
        const string uri = "file:///probe.bas";
        await client.OpenDocumentAsync(uri, "old", ct);
        await ReadFrameAsync(peer, ct); // didOpen
        using var diagnostic = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        Task folding = semantic ? client.RequestSemanticTokensAsync(uri, ct) : client.RequestFoldingRangesAsync(uri, ct);
        using var fold = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        var other = client.RequestFoldingRangesAsync("file:///other.bas", ct);
        using var otherFrame = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        var hover = client.RequestHoverAsync(uri, new Position(0, 0), ct);
        using var hoverFrame = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        var completion = client.RequestCompletionAsync(uri, new Position(0, 0), ct);
        using var completionFrame = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        await client.ChangeDocumentAsync(uri, 2, "new", ct);
        var cancelled = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            using var frame = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
            frame.RootElement.GetProperty("method").GetString().Should().Be("$/cancelRequest");
            cancelled.Add(frame.RootElement.GetProperty("params").GetProperty("id").GetRawText());
        }
        cancelled.Should().BeEquivalentTo(new[] { diagnostic.RootElement.GetProperty("id").GetRawText(),
            fold.RootElement.GetProperty("id").GetRawText() });
        using var change = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        change.RootElement.GetProperty("method").GetString().Should().Be("textDocument/didChange");
        using var fresh = JsonDocument.Parse((await ReadFrameAsync(peer, ct))!);
        fresh.RootElement.GetProperty("method").GetString().Should().Be("textDocument/diagnostic");
        foreach (var frame in new[] { otherFrame, hoverFrame, completionFrame })
            await WriteFrameAsync(peer, "{\"jsonrpc\":\"2.0\",\"id\":"
                + frame.RootElement.GetProperty("id").GetRawText() + ",\"result\":null}", ct);
        await Task.WhenAll(folding, other, hover, completion).WaitAsync(ct);
        logger.ReceivedCalls().Count(call => call.GetMethodInfo().Name == "Log"
            && call.GetArguments()[0] is LogLevel.Warning).Should().Be(0, "superseded analysis is ordinary cancellation");
        peer.Dispose();
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
