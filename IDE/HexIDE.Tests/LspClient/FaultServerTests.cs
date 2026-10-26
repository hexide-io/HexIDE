using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The client's failure paths, driven by a server that misbehaves on demand (#1221).
///
/// <para>
/// Foreign servers (<c>docs/foreign-language-servers.md</c>) find where the client and the specification disagree, and
/// they behave well most of the time, so none of them can be asked to send a broken frame, stop reading, or never answer.
/// This server can, which is the whole of its job: it proves nothing about interoperability, and argues nothing against
/// that document's rule that a second server of our own would prove nothing there. It speaks raw frames rather than
/// through StreamJsonRpc so that it can write what no library would.
/// </para>
///
/// <para>
/// Each test asserts what a user would see: a call that returns, a connection reported lost, a request that
/// comes back empty. A call returning is never taken on its own to mean the server accepted it (CLAUDE.md, #312).
/// Process-level faults (an exit code, a crash of the process, a process left running) need a real process, and stay
/// with the foreign-server tests and a follow-up.
/// </para>
/// </summary>
public sealed class FaultServerTests : IAsyncDisposable
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private readonly List<IAsyncDisposable> _owned = [];

    public async ValueTask DisposeAsync()
    {
        // Bounded, because a client whose stop hangs is exactly what these tests can find, and an unbounded teardown
        // would turn that one failure into a run that never ends.
        foreach (var owned in Enumerable.Reverse(_owned))
        {
            try { await owned.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)); } catch { /* teardown is best effort */ }
        }
    }

    private (VBLspClient Client, FaultServer Server, RecordingLogger Log) Connect(Action<FaultServer>? configure = null)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var server = new FaultServer(serverSide);
        configure?.Invoke(server);
        server.Start();
        _owned.Add(server);

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.CanReconnect.Returns(false);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));
        var log = new RecordingLogger();
        var client = new VBLspClient(transport, log, DocumentLanguage.Vb6, workspace: null,
            initializeTimeout: TimeSpan.FromSeconds(10));
        _owned.Add(client);
        return (client, server, log);
    }

    private static async Task Within(Task task, TimeSpan bound, string because)
    {
        // xUnit1051 suppressed deliberately: the delay is the bound being asserted, not work the test waits on, and a
        // cancellable bound would vanish exactly when a stuck run most needs it (as in SilentServerTests).
#pragma warning disable xUnit1051
        var finished = await Task.WhenAny(task, Task.Delay(bound));
#pragma warning restore xUnit1051
        finished.Should().BeSameAs(task, because);
        await task;
    }

    private static async Task Eventually(Func<bool> condition, TimeSpan bound, string because)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < bound) await Task.Delay(25, Token);
        condition().Should().BeTrue(because);
    }

    private const string Uri = "file:///C:/fault/Module1.bas";

    // ---- Teardown ----

    [Fact]
    public async Task A_server_that_never_answers_shutdown_is_still_sent_exit_and_stop_returns()
    {
        var (client, server, _) = Connect(s => s.AnswerShutdown = false);
        await client.StartAsync(Token);
        client.IsRunning.Should().BeTrue();

        var watch = Stopwatch.StartNew();
        await Within(client.StopAsync(), TimeSpan.FromSeconds(10), "a server that ignores shutdown must not hold the client open");

        watch.Elapsed.Should().BeGreaterThanOrEqualTo(VBLspClient.ShutdownTimeout - TimeSpan.FromMilliseconds(100),
            "the client waits its shutdown deadline for an answer that never comes, and not less");
        await Eventually(() => server.Saw("exit"), TimeSpan.FromSeconds(5), "exit still follows an unanswered shutdown");
        client.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task A_server_that_stops_reading_cannot_hold_stop_open()
    {
        // The deadline on shutdown alone did not bound this. A server that stops reading fills the pipe, the client's
        // next write blocks, and exit, which had no deadline, waited behind it forever (#1221).
        var (client, server, _) = Connect(s => s.ReadsAfterInitialize = false);
        await client.StartAsync(Token);
        await server.StoppedReading.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);

        _ = client.OpenDocumentAsync(Uri, new string('x', 4 * 1024 * 1024), Token); // never completes: nobody reads it

        await Within(client.StopAsync(), TimeSpan.FromSeconds(15),
            "a server that stops reading must not hold the client open, however full the pipe");
        client.IsRunning.Should().BeFalse();
    }

    // ---- Requests ----

    [Fact]
    public async Task A_request_the_server_never_answers_comes_back_empty_at_its_deadline()
    {
        var (client, server, _) = Connect(s => s.Script = (message, _) => Task.FromResult((string?)message["method"] == "textDocument/hover"));
        await client.StartAsync(Token);
        client.RequestTimeout = TimeSpan.FromMilliseconds(500);

        var hover = client.RequestHoverAsync(Uri, new Position(0, 0), Token);
        await Within(hover, TimeSpan.FromSeconds(10), "an unanswered request ends at its deadline");

        (await hover).Should().BeNull();
        server.Saw("textDocument/hover").Should().BeTrue("the request was sent, so it is the answer that never came");
        client.IsRunning.Should().BeTrue("one unanswered request is not a lost connection");
    }

    [Fact]
    public async Task A_message_stalled_halfway_holds_neither_the_request_nor_stop()
    {
        var (client, server, _) = Connect(s => s.Script = async (message, srv) =>
        {
            if ((string?)message["method"] != "textDocument/hover") return false;
            await srv.WriteRawAsync("Content-Length: 200\r\n\r\n{\"jsonrpc\":\"2.0\",\"id\":"u8.ToArray());
            return true; // the rest of the body never comes
        });
        await client.StartAsync(Token);
        client.RequestTimeout = TimeSpan.FromMilliseconds(500);

        var hover = client.RequestHoverAsync(Uri, new Position(0, 0), Token);
        await Within(hover, TimeSpan.FromSeconds(10), "a reply that stops halfway ends at the request's deadline");
        (await hover).Should().BeNull();

        await Within(client.StopAsync(), TimeSpan.FromSeconds(15), "stopping does not wait for the rest of a stalled message");
    }

    [Fact]
    public async Task A_server_that_dies_halfway_through_a_reply_fails_the_request_at_once()
    {
        var (client, server, log) = Connect(s => s.Script = async (message, srv) =>
        {
            if ((string?)message["method"] != "textDocument/hover") return false;
            await srv.WriteRawAsync("Content-Length: 200\r\n\r\n{\"jsonrpc\":\"2.0\""u8.ToArray());
            srv.Hangup();
            return true;
        });
        await client.StartAsync(Token);
        client.RequestTimeout = TimeSpan.FromSeconds(60);

        var hover = client.RequestHoverAsync(Uri, new Position(0, 0), Token);
        // Well inside the 60 s deadline, so what ends it is the lost connection, not the deadline.
        await Within(hover, TimeSpan.FromSeconds(10), "a connection that drops fails its pending request then, not at its deadline");
        (await hover).Should().BeNull();

        await Eventually(() => !client.IsRunning, TimeSpan.FromSeconds(5), "the client knows the connection is gone");
        log.Has(LogLevel.Warning, "connection lost").Should().BeTrue("a server falling over is reported, not silent");
    }

    // ---- Malformed frames ----

    [Theory]
    [InlineData("a Content-Length that is not a number", "Content-Length: many\r\n\r\n{}")]
    [InlineData("a body that is not JSON", "Content-Length: 5\r\n\r\n{abc}")]
    [InlineData("no Content-Length at all", "Content-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n{}")]
    public async Task A_malformed_frame_ends_the_connection_and_is_reported(string what, string frame)
    {
        _ = what;
        var (client, server, log) = Connect();
        await client.StartAsync(Token);

        await server.WriteRawAsync(Encoding.UTF8.GetBytes(frame));

        await Eventually(() => !client.IsRunning, TimeSpan.FromSeconds(5), "a stream that can no longer be framed is a lost connection");
        log.Has(LogLevel.Warning, "connection lost").Should().BeTrue("the client says why it stopped");
        await Within(client.StopAsync(), TimeSpan.FromSeconds(10), "stopping after a broken stream returns");
    }

    // ---- Volume ----

    [Fact]
    public async Task A_flood_of_notifications_does_not_starve_a_request()
    {
        var (client, server, _) = Connect(s => s.Script = async (message, srv) =>
        {
            if ((string?)message["method"] != "textDocument/hover") return false;
            await srv.ReplyAsync(message["id"], new JsonObject
            {
                ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = "answered" },
            });
            return true;
        });
        await client.StartAsync(Token);

        for (var i = 0; i < 5000; i++)
            await server.SendAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["method"] = "window/logMessage",
                ["params"] = new JsonObject { ["type"] = 4, ["message"] = $"log line {i}" },
            });

        var hover = client.RequestHoverAsync(Uri, new Position(0, 0), Token);
        await Within(hover, TimeSpan.FromSeconds(20), "five thousand notifications in the way do not stop a reply arriving");
        (await hover).Should().NotBeNull("the reply behind the flood is read, not dropped");
        client.IsRunning.Should().BeTrue();
    }

    // ---- Server-to-client requests ----

    [Fact]
    public async Task A_server_request_the_client_does_not_support_is_answered_method_not_found()
    {
        var (client, server, _) = Connect();
        await client.StartAsync(Token);

        await server.SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = "srv-1", ["method"] = "workspace/somethingNobodyImplements" });

        // The client's own requests carry numeric ids, so match the string one without casting every id to a string.
        static bool IsTheAnswer(JsonNode m) => m["id"] is JsonValue id && id.TryGetValue<string>(out var s) && s == "srv-1";
        await Eventually(() => server.Received.Any(IsTheAnswer), TimeSpan.FromSeconds(5),
            "a request is answered, even when the answer is no; a server may wait on it");
        var answer = server.Received.First(IsTheAnswer);
        ((int?)answer["error"]?["code"]).Should().Be(-32601, "an unsupported method is MethodNotFound on the wire");
        client.IsRunning.Should().BeTrue("one unsupported request is not a reason to drop the connection");
    }

    // ---- The harness ----

    /// <summary>
    /// A server over one end of an in-memory duplex stream, scripted per test. By default it answers <c>initialize</c>
    /// with document sync and hover, answers <c>shutdown</c>, ends on <c>exit</c>, and answers any other request with a
    /// null result. A test's <see cref="Script"/> sees each message first and returns true for the ones it has handled.
    /// </summary>
    private sealed class FaultServer(Stream stream) : IAsyncDisposable
    {
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly List<JsonNode> _received = [];
        private readonly CancellationTokenSource _stop = new();
        private Task _loop = Task.CompletedTask;

        public Func<JsonNode, FaultServer, Task<bool>>? Script { get; set; }
        public bool AnswerShutdown { get; set; } = true;
        public bool ReadsAfterInitialize { get; set; } = true;
        public TaskCompletionSource StoppedReading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<JsonNode> Received { get { lock (_received) return [.. _received]; } }
        public bool Saw(string method) => Received.Any(m => (string?)m["method"] == method);

        public void Start() => _loop = Task.Run(ReadLoopAsync);

        public void Hangup() => stream.Dispose();

        public async Task WriteRawAsync(byte[] bytes)
        {
            await _writeGate.WaitAsync(Token);
            try
            {
                await stream.WriteAsync(bytes, Token);
                await stream.FlushAsync(Token);
            }
            finally { _writeGate.Release(); }
        }

        public Task SendAsync(JsonNode message)
        {
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            return WriteRawAsync([.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body]);
        }

        public Task ReplyAsync(JsonNode? id, JsonNode? result) =>
            SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

        private async Task ReadLoopAsync()
        {
            try
            {
                while (await ReadFrameAsync() is { } body)
                {
                    var message = JsonNode.Parse(body)!;
                    lock (_received) _received.Add(message);
                    if (Script is not null && await Script(message, this)) continue;
                    switch ((string?)message["method"])
                    {
                        case "initialize":
                            await ReplyAsync(message["id"], new JsonObject
                            {
                                ["capabilities"] = new JsonObject
                                {
                                    ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 1 },
                                    ["hoverProvider"] = true,
                                },
                            });
                            if (!ReadsAfterInitialize) { StoppedReading.TrySetResult(); return; }
                            break;
                        case "shutdown":
                            if (AnswerShutdown) await ReplyAsync(message["id"], null);
                            break;
                        case "exit":
                            return;
                        default:
                            if (message["id"] is not null && message["method"] is not null) await ReplyAsync(message["id"], null);
                            break;
                    }
                }
            }
            catch (Exception) when (_stop.IsCancellationRequested) { /* torn down */ }
            catch (ObjectDisposedException) { /* hung up */ }
            catch (IOException) { /* the client went away */ }
        }

        private async Task<string?> ReadFrameAsync()
        {
            var header = new List<byte>();
            var one = new byte[1];
            while (!(header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n'))
            {
                if (await stream.ReadAsync(one, _stop.Token) == 0) return null;
                header.Add(one[0]);
            }
            var length = Encoding.ASCII.GetString([.. header]).Split("\r\n")
                .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                .Select(line => int.Parse(line["Content-Length:".Length..].Trim())).First();
            var body = new byte[length];
            await stream.ReadExactlyAsync(body, _stop.Token);
            return Encoding.UTF8.GetString(body);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            stream.Dispose();
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
            _stop.Dispose();
            _writeGate.Dispose();
        }
    }

    /// <summary>What was logged, at what level: a substitute's ReceivedWithAnyArgs would match any level (SilentServerTests).</summary>
    private sealed class RecordingLogger : ILogger<VBLspClient>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];
        public bool Has(LogLevel level, string text) { lock (_entries) return _entries.Any(e => e.Level == level && e.Message.Contains(text, StringComparison.OrdinalIgnoreCase)); }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
