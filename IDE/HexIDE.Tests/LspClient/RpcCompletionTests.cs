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
    public async Task Process_exit_leaves_no_unobserved_task(string mode)
    {
        CollectFinalizers();
        var faults = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs e) => faults.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            await new StdioProcessFaultTests().Process_lifecycle_is_observable_and_stop_leaves_no_server(mode);
            CollectFinalizers();
            faults.Should().BeEmpty();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }
    }

    private static void CollectFinalizers()
    {
        for (var i = 0; i < 2; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [Fact]
    public async Task WebSocket_close_and_reconnect_leave_no_unobserved_task()
    {
        CollectFinalizers();
        var faults = new List<Exception>();
        void Record(object? sender, UnobservedTaskExceptionEventArgs e) => faults.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += Record;
        try
        {
            await CloseAndReconnectAsync();
            CollectFinalizers();
            faults.Should().BeEmpty();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Record;
        }
    }

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
