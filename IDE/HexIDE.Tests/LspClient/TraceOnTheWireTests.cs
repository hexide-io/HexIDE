using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What asking a server to describe its own work looks like on the wire, and what comes back.
///
/// <para>
/// <b>Read the bytes, for the reason its sibling does.</b> A <c>[JsonRpcMethod]</c> handler is told its
/// arguments were bound and cannot report what the frame actually carried, which is the whole question for
/// a field that is optional and whose absence is meaningful. <c>ShutdownWireShapeTests</c> learned that the
/// expensive way.
/// </para>
///
/// <para>
/// There is a second reason here. This half of the protocol cannot be proved against anything real: five
/// servers driven with verbose tracing and an explicit <c>$/setTrace</c> emitted <b>zero</b> trace
/// notifications between them, and three of those binaries contain no trace machinery at all. So until the
/// bundled server grows its own, a hand-spoken server is the only conformant peer that exists.
/// </para>
/// </summary>
public class TraceOnTheWireTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));
    private readonly List<JsonDocument> _received = [];
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Stream? _serverSide;

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        _cts.Dispose();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private JsonElement FrameFor(string method)
    {
        lock (_received)
        {
            return _received
                .Select(d => d.RootElement)
                .FirstOrDefault(e => e.TryGetProperty("method", out var m) && m.GetString() == method);
        }
    }

    /// <summary>A minimal server, spoken by hand: it answers initialize, records everything, interprets nothing.</summary>
    private async Task<VBLspClient> ConnectedAsync(string trace = LspTraceValue.Off, bool start = true)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        _serverSide = serverSide;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var json = await ReadFrameAsync(serverSide, _cts.Token);
                    if (json is null) return;

                    var doc = JsonDocument.Parse(json);
                    lock (_received) _received.Add(doc);

                    if (!doc.RootElement.TryGetProperty("method", out var m)) continue;
                    if (!doc.RootElement.TryGetProperty("id", out var id)) continue;   // notification

                    var head = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":";
                    var reply = m.GetString() == "initialize"
                        ? head + "{\"capabilities\":{},\"serverInfo\":"
                               + "{\"name\":\"trace-probe\",\"version\":\"1.0.0\"}}}"
                        : head + "null}";
                    await SendAsync(reply);
                }
            }
            catch (OperationCanceledException) { /* the test finished */ }
            catch (IOException) { /* the client hung up */ }
        }, _cts.Token);

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(
            transport, Substitute.For<ILogger<VBLspClient>>(), "vb6", trace: trace);
        _disposables.Add(client);
        if (start) await client.StartAsync();
        return client;
    }

    private async Task SendAsync(string json)
    {
        await _writeLock.WaitAsync(_cts.Token);
        try { await WriteFrameAsync(_serverSide!, json, _cts.Token); }
        finally { _writeLock.Release(); }
    }

    /// <summary>Waits for a condition the far end satisfies asynchronously, rather than sleeping at it.</summary>
    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        condition().Should().BeTrue("the expected frame never arrived within ten seconds");
    }

    // ── Outbound: what the client says ───────────────────────────────────────

    [Fact]
    public async Task TheConfiguredLevelTravelsInTheHandshake()
    {
        await ConnectedAsync(LspTraceValue.Verbose);

        var initialize = FrameFor("initialize");
        initialize.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        initialize.GetProperty("params").GetProperty("trace").GetString()
            .Should().Be("verbose");
    }

    [Fact]
    public async Task OffIsStatedRatherThanOmitted()
    {
        // The field is optional and off is its default, so this changes nothing about how a server behaves.
        // It changes what a capture of a quiet session can say: "we asked for nothing" rather than the
        // reader having to infer it from an absence.
        await ConnectedAsync();

        FrameFor("initialize").GetProperty("params").GetProperty("trace").GetString()
            .Should().Be("off");
    }

    [Fact]
    public async Task ChangingTheLevelSendsTheProtocolsNotification()
    {
        var client = await ConnectedAsync();

        await client.SetTraceAsync(LspTraceValue.Messages, TestContext.Current.CancellationToken);

        await EventuallyAsync(() => FrameFor("$/setTrace").ValueKind != JsonValueKind.Undefined);
        var frame = FrameFor("$/setTrace");
        frame.GetProperty("params").GetProperty("value").GetString().Should().Be("messages");
        frame.TryGetProperty("id", out _).Should().BeFalse(
            "$/setTrace is a notification; sending it as a request would wait forever for a reply the "
          + "protocol never defines");
    }

    [Fact]
    public async Task ALevelNobodyDefinedIsNotSentAtAll()
    {
        // Refused rather than coerced to off. Silently downgrading would leave a developer who mistyped
        // 'verbose' watching an empty trace and concluding the server ignores them.
        var client = await ConnectedAsync();

        await client.SetTraceAsync("chatty", TestContext.Current.CancellationToken);

        FrameFor("$/setTrace").ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task ALevelSetBeforeTheServerStartedIsCarriedIntoTheHandshake()
    {
        // The level belongs to the connection rather than to the process. Setting it while nothing is
        // connected must not be lost, or a reconnect would silently revert to whatever configuration said
        // months ago rather than what the developer chose two minutes earlier.
        var client = await ConnectedAsync(start: false);

        await client.SetTraceAsync(LspTraceValue.Verbose, TestContext.Current.CancellationToken);
        await client.StartAsync(TestContext.Current.CancellationToken);

        FrameFor("initialize").GetProperty("params").GetProperty("trace").GetString()
            .Should().Be("verbose");
    }

    // ── Inbound: what the client does with the answer ────────────────────────

    [Fact]
    public async Task AServersOwnTraceIsRaisedRatherThanSwallowed()
    {
        var client = await ConnectedAsync(LspTraceValue.Verbose);

        LogTraceParams? seen = null;
        client.TraceReceived += (_, p) => seen = p;

        await SendAsync("""
            {"jsonrpc":"2.0","method":"$/logTrace",
             "params":{"message":"Parsed Module1 in 41ms","verbose":"fast path; 0 diagnostics; 7 symbols"}}
            """);

        await EventuallyAsync(() => seen is not null);
        seen!.Message.Should().Be("Parsed Module1 in 41ms");
        seen.Verbose.Should().Be("fast path; 0 diagnostics; 7 symbols");
    }

    [Fact]
    public async Task ATraceWithoutDetailIsStillRaised()
    {
        // The verbose member is absent at the `messages` level, which is the entire difference between the
        // two levels that are not off. A reader that required it would drop half the protocol.
        var client = await ConnectedAsync(LspTraceValue.Messages);

        LogTraceParams? seen = null;
        client.TraceReceived += (_, p) => seen = p;

        await SendAsync("""{"jsonrpc":"2.0","method":"$/logTrace","params":{"message":"Parsed Module1"}}""");

        await EventuallyAsync(() => seen is not null);
        seen!.Message.Should().Be("Parsed Module1");
        seen.Verbose.Should().BeNull();
    }

    [Fact]
    public async Task AServersRunningCommentaryIsNowReachable()
    {
        // This reverses a decision recorded on ILspClient: window/logMessage deliberately had no event,
        // on the grounds that it was detail for a log. It is — and where a thing should be shown is not the
        // same question as whether a consumer can reach it, which is the one a transport contract answers.
        var client = await ConnectedAsync();

        LogMessageParams? seen = null;
        client.MessageLogged += (_, p) => seen = p;

        await SendAsync("""
            {"jsonrpc":"2.0","method":"window/logMessage",
             "params":{"type":3,"message":"No compilation database found; using a fallback command"}}
            """);

        await EventuallyAsync(() => seen is not null);
        seen!.Message.Should().Be("No compilation database found; using a fallback command");
        seen.Type.Should().Be(LspMessageType.Info);
    }

    [Fact]
    public async Task A318DebugMessageIsReadRatherThanRejected()
    {
        // 3.18 added a fifth severity. A client that did not know it would still have to carry the
        // message, because refusing to parse the frame would take the connection down over a number.
        var client = await ConnectedAsync();

        LogMessageParams? seen = null;
        client.MessageLogged += (_, p) => seen = p;

        await SendAsync("""
            {"jsonrpc":"2.0","method":"window/logMessage","params":{"type":5,"message":"cache hit"}}
            """);

        await EventuallyAsync(() => seen is not null);
        seen!.Type.Should().Be(LspMessageType.Debug);
    }

    // ---- LSP framing, by hand ------------------------------------------------------------------

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

        var text = Encoding.ASCII.GetString([.. header]);
        var length = text
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            .Select(line => int.Parse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture))
            .FirstOrDefault();

        if (length <= 0) return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var got = await stream.ReadAsync(body.AsMemory(read, length - read), ct);
            if (got == 0) return null;
            read += got;
        }

        return Encoding.UTF8.GetString(body);
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n\r\n"));
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
