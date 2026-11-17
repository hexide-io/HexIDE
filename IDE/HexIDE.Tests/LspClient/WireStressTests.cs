using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class WireStressTests(ITestOutputHelper output)
{
    private sealed class Ledger
    {
        public readonly ConcurrentQueue<string> Failures = new();
        public readonly Dictionary<string, Queue<byte[]>> Starts = [];
        public long Frames;
        public readonly ConcurrentDictionary<string, int> Methods = new();

        public void Remember(string side, byte[] bytes)
        {
            lock (Starts)
            {
                if (!Starts.TryGetValue(side, out var entries)) Starts[side] = entries = new();
                entries.Enqueue(bytes);
                while (entries.Count > 64) entries.Dequeue();
            }
        }

        public string Matches(ReadOnlySpan<byte> bytes)
        {
            var matches = new List<string>();
            var copy = bytes.ToArray();
            lock (Starts)
                foreach (var (side, entries) in Starts)
                    if (entries.Any(start => copy.AsSpan().StartsWith(start)))
                        matches.Add(side);
            return matches.Count == 0 ? "none" : string.Join(", ", matches);
        }
    }

    // Independent incremental framing: never alter the chunks handed to StreamJsonRpc.
    private sealed class Frames(Ledger ledger, string side, bool writing)
    {
        private readonly List<byte> header = [];
        private readonly Queue<byte> recent = new();
        private byte[]? body;
        private int used;
        private bool failed;
        private static string Escape(byte[] bytes) => string.Concat(bytes.Select(b => b switch
        {
            13 => "\\r", 10 => "\\n", 9 => "\\t", 92 => "\\\\",
            >= 32 and <= 126 => ((char)b).ToString(),
            _ => "\\x" + b.ToString("X2", CultureInfo.InvariantCulture)
        }));

        public void Feed(ReadOnlySpan<byte> bytes)
        {
            lock (this)
                foreach (var b in bytes)
                {
                    recent.Enqueue(b);
                    if (recent.Count > 4096) recent.Dequeue();
                    if (failed) continue;
                    if (body is null)
                    {
                        header.Add(b);
                        if (header.Count > 8192) { Bad(0, "header too long", [.. header]); continue; }
                        if (header.Count < 4 || !header.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) continue;
                        var lines = Encoding.ASCII.GetString([.. header]).Split("\r\n")
                            .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).ToArray();
                        if (lines.Length != 1 || !int.TryParse(lines[0][15..].Trim(), CultureInfo.InvariantCulture, out var length)
                            || length <= 0 || length > 4 * 1024 * 1024)
                        { Bad(0, "invalid Content-Length", [.. header]); continue; }
                        body = new byte[length];
                        used = 0;
                        if (writing) ledger.Remember(side, [.. header]);
                    }
                    else
                    {
                        body[used++] = b;
                        if (used != body.Length) continue;
                        try
                        {
                            using var json = JsonDocument.Parse(body);
                            if (!writing)
                            {
                                Interlocked.Increment(ref ledger.Frames);
                                if (json.RootElement.ValueKind == JsonValueKind.Object
                                    && json.RootElement.TryGetProperty("method", out var method))
                                    ledger.Methods.AddOrUpdate(method.GetString()!, 1, (_, count) => count + 1);
                            }
                        }
                        catch (JsonException e)
                        {
                            var offset = 0;
                            for (long line = 0; line < e.LineNumber; offset++)
                                if (body[offset] == 10) line++;
                            offset += (int)(e.BytePositionInLine ?? 0);
                            Bad(offset, e.Message, body);
                        }
                        body = null;
                        header.Clear();
                    }
                }
        }

        private void Bad(int offset, string reason, byte[] bytes)
        {
            failed = true;
            var at = Math.Clamp(offset, 0, bytes.Length);
            ledger.Failures.Enqueue($"#3520 pair/side {side} {(writing ? "write" : "read")}: header={Escape([.. header])}; "
                + $"offset={offset}; around={Escape(bytes[Math.Max(0, at - 64)..Math.Min(bytes.Length, at + 128)])}; "
                + $"frame-start matches={ledger.Matches(bytes.AsSpan(at))}; reason={reason}; last4096={Escape([.. recent])}");
        }

        public void End()
        {
            lock (this)
                if (!failed && (header.Count != 0 || body is not null))
                    Bad(used, "truncated frame at EOF", body is null ? [.. header] : body[..used]);
        }
    }

    private sealed class CheckingStream(Stream inner, Ledger ledger, string side) : Stream
    {
        private readonly Frames reads = new(ledger, side, false);
        private readonly Frames writes = new(ledger, side, true);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var n = await inner.ReadAsync(buffer, cancellationToken);
            reads.Feed(buffer.Span[..n]);
            if (n == 0) reads.End();
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            reads.Feed(buffer.AsSpan(offset, n));
            if (n == 0) reads.End();
            return n;
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            writes.Feed(buffer.Span);
            return inner.WriteAsync(buffer, cancellationToken);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Write(byte[] buffer, int offset, int count)
        { writes.Feed(buffer.AsSpan(offset, count)); inner.Write(buffer, offset, count); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

    private sealed class Server
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["capabilities"] = new Dictionary<string, object> { ["textDocumentSync"] = 1, ["hoverProvider"] = true, ["documentSymbolProvider"] = true }
        });
        [JsonRpcMethod("initialized")] public void Initialized(JsonElement _) { }
        [JsonRpcMethod("textDocument/didOpen")] public void Open(JsonElement _) { }
        [JsonRpcMethod("textDocument/didChange")] public void Change(JsonElement _) { }
        [JsonRpcMethod("textDocument/didClose")] public void Close(JsonElement _) { }
        [JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Hover(JsonElement _, CancellationToken cancellationToken)
        {
            await Task.Delay(5, cancellationToken);
            return JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["contents"] = "stress" });
        }
        [JsonRpcMethod("textDocument/documentSymbol", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Symbols(JsonElement _) => JsonSerializer.SerializeToElement(Array.Empty<int>());
        [JsonRpcMethod("shutdown")] public object? Shutdown() => null;
        [JsonRpcMethod("exit")] public void Exit() { }
    }

    private static int Setting(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var n) && n > 0 ? n : fallback;

    // Opt-in: the load deliberately occupies thread-pool workers, and test classes run in parallel, so in the
    // default suite it could starve the in-memory pairs of other classes, which is the kind of flake it hunts.
    public static bool StressRequested => Setting("HEXIDE_WIRE_STRESS_SECONDS", 0) > 0;

    [Fact(Skip = "Set HEXIDE_WIRE_STRESS_SECONDS to run the wire stress test (#3520)", SkipUnless = nameof(StressRequested))]
    public async Task ConcurrentConnectionsKeepEveryFrameValid()
    {
        var seconds = Setting("HEXIDE_WIRE_STRESS_SECONDS", 3);
        var pairs = Setting("HEXIDE_WIRE_STRESS_PAIRS", 8);
        var ledger = new Ledger();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(seconds + 30));
        var token = deadline.Token;
        var clients = new List<VBLspClient>();
        var peers = new List<JsonRpc>();
        using var release = new ManualResetEventSlim();
        var blockers = new List<Task>();
        Thread? releaser = null;
        try
        {
            for (var i = 0; i < pairs; i++)
            {
                var (local, remote) = FullDuplexStream.CreatePair();
                var a = new CheckingStream(local, ledger, $"{i}/client");
                var b = new CheckingStream(remote, ledger, $"{i}/server");
                var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(b, b, new SystemTextJsonFormatter()), new Server());
                peers.Add(rpc);
                rpc.StartListening();
                var transport = Substitute.For<ILspTransport>();
                transport.IsAlive.Returns(true);
                transport.LeaveServerRunning.Returns(true);
                transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
                    .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(a, a, c.Arg<IJsonRpcMessageFormatter>())));
                clients.Add(new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6"));
            }
            await Task.WhenAll(clients.Select(c => c.StartAsync(token)));
            clients.Should().OnlyContain(c => c.IsRunning);
            var clock = Stopwatch.StartNew();
            var load = Task.WhenAll(clients.Select(async (client, i) =>
            {
                var version = 0;
                while (clock.Elapsed.TotalSeconds < seconds)
                {
                    var uri = $"file:///stress-{i}-{version / 4}.bas";
                    var text = new string((char)('a' + i % 26), 16384 + (int)((version * 7919L + i * 997) % 49153));
                    await client.OpenDocumentAsync(uri, text, token);
                    await client.ChangeDocumentAsync(uri, ++version, text + version, token);
                    using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
                    var hover = client.RequestHoverAsync(uri, new Position(0, 0), cancel.Token);
                    if (version % 2 == 0) cancel.Cancel();
                    var rpc = peers[i];
                    await Task.WhenAll(hover, client.RequestDocumentSymbolsAsync(uri, token),
                        rpc.InvokeWithParameterObjectAsync<JsonElement>("workspace/configuration", JsonSerializer.SerializeToElement(new { items = new[] { new { section = "stress" } } }), token),
                        rpc.InvokeWithParameterObjectAsync<JsonElement>("workspace/applyEdit", JsonSerializer.SerializeToElement(new { edit = new { changes = new Dictionary<string, object>() } }), token),
                        rpc.NotifyWithParameterObjectAsync("window/logMessage", JsonSerializer.SerializeToElement(new { type = 3, message = text[..256] })));
                    if (version % 4 == 0) await client.CloseDocumentAsync(uri, token);
                }
            }));
            // Occupy a bounded set of workers during the middle of the load, releasing from a dedicated thread.
            for (var i = 0; i < Math.Min(16, Math.Max(4, Environment.ProcessorCount)); i++)
                blockers.Add(Task.Run(() => release.Wait(token), token));
            releaser = new Thread(() => { release.Wait(TimeSpan.FromSeconds(seconds / 2.0)); release.Set(); });
            releaser.Start();
            await load;
            await Task.WhenAll(blockers);
        }
        finally
        {
            release.Set();
            releaser?.Join();
            foreach (var client in clients) await client.DisposeAsync();
            foreach (var peer in peers) peer.Dispose();
            output.WriteLine($"#3520: pairs={pairs}; seconds={seconds}; checked frames={ledger.Frames}; bad={ledger.Failures.Count}");
            ledger.Failures.Should().BeEmpty(string.Join(Environment.NewLine, ledger.Failures));
        }
        ledger.Frames.Should().BeGreaterThan(pairs * 10);
        foreach (var method in new[] { "textDocument/didOpen", "textDocument/didChange", "textDocument/hover",
                     "textDocument/documentSymbol", "$/cancelRequest", "workspace/configuration", "workspace/applyEdit", "window/logMessage" })
            ledger.Methods.Should().ContainKey(method);
    }

    [Fact]
    public void CheckerAcceptsFragmentedAndCoalescedFramesAndMatchesForeignHeaders()
    {
        var ledger = new Ledger();
        var frame = Encoding.UTF8.GetBytes("Content-Length: 10\r\n\r\n{\"x\":\"é\"}");
        var writer = new Frames(ledger, "7/client", true);
        writer.Feed(frame);
        var reader = new Frames(ledger, "7/server", false);
        foreach (var b in frame) reader.Feed(new byte[] { b });
        reader.Feed(frame.Concat(frame).ToArray());
        reader.End();
        ledger.Failures.Should().BeEmpty();
        ledger.Frames.Should().Be(3);
        ledger.Matches(frame).Should().Be("7/client");
        ledger.Matches(Encoding.ASCII.GetBytes("unrelated")).Should().Be("none");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckerDetectsCorruptAndTruncatedFrames(bool truncated)
    {
        var ledger = new Ledger();
        var frames = new Frames(ledger, "3/server", false);
        frames.Feed(Encoding.ASCII.GetBytes("Content-Length: 20\r\n\r\n" + (truncated ? "{" : "{\"x\":Content-Length: ")));
        frames.End();
        ledger.Failures.Should().ContainSingle().Which.Should().Contain("3/server").And.Contain("offset=").And.Contain("last4096=");
    }
}
