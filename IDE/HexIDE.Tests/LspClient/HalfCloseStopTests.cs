using System.Diagnostics;
using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging.Abstractions;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class HalfCloseStopTests
{
    private sealed class Server
    {
        public readonly TaskCompletionSource ExitReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonSerializer.SerializeToElement(new Dictionary<string, object> { ["capabilities"] = new Dictionary<string, object>() });
        [JsonRpcMethod("initialized")] public void Initialized(JsonElement _) { }
        [JsonRpcMethod("shutdown")] public object? Shutdown() => null;
        [JsonRpcMethod("exit")] public void Exit() => ExitReceived.TrySetResult();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopEndsOutputAndWaitsForPeerBeforeDisposing(bool peerStaysOpen)
    {
        var token = TestContext.Current.CancellationToken;
        var (local, remote) = FullDuplexStream.CreatePair();
        var server = new Server();
        using var peer = new JsonRpc(new LspMessageHandler(remote, remote, new SystemTextJsonFormatter()), server);
        peer.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(new LspMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.TryEndOutput().Returns(_ => { ended.TrySetResult(); return true; });
        var disposed = false;
        transport.DisposeAsync().Returns(_ => { disposed = true; return ValueTask.CompletedTask; });
        await using var client = new VBLspClient(transport, NullLogger<VBLspClient>.Instance, "vb6");
        await client.StartAsync(token);
        var watch = Stopwatch.StartNew();
        var stopping = client.StopAsync();
        await server.ExitReceived.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        disposed.Should().BeFalse("ending output precedes RPC and transport disposal");
        stopping.IsCompleted.Should().BeFalse("the server's output has not ended yet");
        if (!peerStaysOpen) peer.Dispose();
        await stopping.WaitAsync(VBLspClient.ServerOutputEndTimeout + TimeSpan.FromSeconds(3), token);
        disposed.Should().BeTrue();
        transport.Received(1).TryEndOutput();
        if (peerStaysOpen)
            watch.Elapsed.Should().BeGreaterThanOrEqualTo(VBLspClient.ServerOutputEndTimeout - TimeSpan.FromMilliseconds(50));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedAndLeaveRunningConnectionsDoNotWait(bool leaveRunning)
    {
        var token = TestContext.Current.CancellationToken;
        var (local, remote) = FullDuplexStream.CreatePair();
        var server = new Server();
        using var peer = new JsonRpc(new LspMessageHandler(remote, remote, new SystemTextJsonFormatter()), server);
        peer.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(leaveRunning);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(new LspMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
        await using var client = new VBLspClient(transport, NullLogger<VBLspClient>.Instance, "vb6");
        await client.StartAsync(token);
        await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(3), token);
        if (leaveRunning) transport.DidNotReceive().TryEndOutput();
        else transport.Received(1).TryEndOutput();
        if (!leaveRunning) await server.ExitReceived.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        else server.ExitReceived.Task.IsCompleted.Should().BeFalse();
    }
}
