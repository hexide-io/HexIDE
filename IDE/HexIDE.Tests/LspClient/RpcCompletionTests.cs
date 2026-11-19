using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class RpcCompletionTests
{
    [Theory]
    [InlineData("crash")]
    [InlineData("nonzero")]
    [InlineData("exit1")]
    [InlineData("exit2")]
    public async Task Process_exit_leaves_no_unobserved_task(string mode) =>
        (await OwnUnobservedFaultsDuring(() =>
            new StdioProcessFaultTests().Process_lifecycle_is_observable_and_stop_leaves_no_server(mode))).Should().BeEmpty();

    [Fact]
    public async Task A_fault_that_read_through_an_in_memory_pipe_is_another_tests()
    {
        var pipe = new System.IO.Pipelines.Pipe();
        await pipe.Reader.CompleteAsync();
        var readCompletedPipe = async () => await pipe.Reader.AsStream().ReadAsync(new byte[1], TestContext.Current.CancellationToken);
        var inMemory = (await readCompletedPipe.Should().ThrowAsync<InvalidOperationException>()).Which;
        ReadThroughAnInMemoryPipe(new AggregateException(inMemory)).Should().BeTrue();

        var endOfStream = async () => { await Task.Yield(); throw new EndOfStreamException(); };
        var own = (await endOfStream.Should().ThrowAsync<EndOfStreamException>()).Which;
        ReadThroughAnInMemoryPipe(new AggregateException(own)).Should().BeFalse();
    }

    // The unobserved faults of this test's own connection while run runs (#4875). The event is process-wide, so a read loop
    // another test leaked can fault and be collected inside this window (#4833), and it used to fail this class on PRs that
    // never touched it (#4941). This class's connections read a child process's standard output or a WebSocket, never an
    // in-memory pipe, so a fault that read through one is another test's stand-in, and isn't counted.
    private static async Task<List<Exception>> OwnUnobservedFaultsDuring(Func<Task> run)
    {
        CollectFinalizers();
        var faults = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (!ReadThroughAnInMemoryPipe(e.Exception))
                lock (faults) faults.Add(e.Exception);
        }
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            await run();
            CollectFinalizers();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }
        lock (faults) return [.. faults];
    }

    // System.IO.Pipelines' PipeReaderStream is what Pipe.Reader.AsStream() returns: an in-memory pipe's end.
    private static bool ReadThroughAnInMemoryPipe(AggregateException fault) =>
        fault.Flatten().InnerExceptions.Any(e =>
            e.StackTrace?.Contains("System.IO.Pipelines.PipeReaderStream.", StringComparison.Ordinal) == true);

    private static void CollectFinalizers()
    {
        for (var i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [Fact]
    public async Task WebSocket_close_and_reconnect_leave_no_unobserved_task() =>
        (await OwnUnobservedFaultsDuring(CloseAndReconnectAsync)).Should().BeEmpty();

    private static async Task CloseAndReconnectAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        using var port = new TcpListener(IPAddress.Loopback, 0);
        port.Start();
        var endpoint = new Uri($"ws://localhost:{((IPEndPoint)port.LocalEndpoint).Port}/lsp/");
        port.Stop();
        var logger = Substitute.For<ILogger<VBLspClient>>();
        var transport = new WebSocketLspTransport(endpoint.ToString(),
            Substitute.For<ILogger<WebSocketLspTransport>>(), WebSocketRole.Listen);
        await using var client = new VBLspClient(transport, logger, "completion");
        var starting = client.StartAsync(token);
        using var first = new ClientWebSocket();
        await first.ConnectAsync(endpoint, token);
        using var firstRpc = new JsonRpc(new WebSocketMessageHandler(first, new SystemTextJsonFormatter()), new Server());
        firstRpc.StartListening();
        await starting.WaitAsync(token);
        client.IsRunning.Should().BeTrue();
        // An abrupt peer close faults the real WebSocket receive loop.
        first.Abort();
        try { await firstRpc.Completion.WaitAsync(token); }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException || ex is OperationCanceledException && !token.IsCancellationRequested) { }
        while (client.IsRunning) await Task.Delay(10, token);

        // ReconnectLoopAsync disposes the old RPC before listening again.
        using var second = await ConnectAgainAsync();
        var server = new Server(second.Abort);
        using var secondRpc = new JsonRpc(new WebSocketMessageHandler(second, new SystemTextJsonFormatter()), server);
        secondRpc.StartListening();
        await server.Initialized.Task.WaitAsync(token);
        while (!client.IsRunning) await Task.Delay(10, token);
        client.IsRunning.Should().BeTrue();
        await client.StopAsync().WaitAsync(token);
        try { await secondRpc.Completion.WaitAsync(token); }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException || ex is OperationCanceledException && !token.IsCancellationRequested) { }

        async Task<ClientWebSocket> ConnectAgainAsync()
        {
            while (true)
            {
                var socket = new ClientWebSocket();
                try
                {
                    await socket.ConnectAsync(endpoint, token);
                    return socket;
                }
                catch (WebSocketException)
                {
                    socket.Dispose();
                    await Task.Delay(100, token);
                }
            }
        }
    }

    private sealed class Server(Action? exit = null)
    {
        public TaskCompletionSource Initialized { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse("""{"capabilities":{}}""").RootElement.Clone();

        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void OnInitialized(JsonElement _) => Initialized.TrySetResult();

        [JsonRpcMethod("shutdown")]
        public object? Shutdown() => null;

        [JsonRpcMethod("exit")]
        public void Exit() => exit?.Invoke();
    }
}
