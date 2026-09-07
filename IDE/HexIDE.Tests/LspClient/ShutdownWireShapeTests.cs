using System.Globalization;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What <c>shutdown</c> and <c>exit</c> look like on the wire.
///
/// <para>
/// LSP 3.17 defines both as taking <b>no parameters</b>, and the three ways of expressing "nothing" are not
/// interchangeable to a real server. Measured against the three foreign servers this suite drives, and
/// against a fourth, externally-authored one:
/// </para>
///
/// <list type="table">
/// <item><description><c>"params":[]</c> — REJECTED by rumdl (tower-lsp) and by OmniSharp-based servers.
///   This is what <c>JsonRpc.InvokeAsync(name)</c> sends, because it is the <b>positional</b>
///   overload.</description></item>
/// <item><description><c>"params":{}</c> — REJECTED by rumdl. This is what <c>EmptyParams.Instance</c>
///   sends.</description></item>
/// <item><description>member omitted — accepted by every server measured, and what the specification and
///   <c>vscode-languageserver-node</c> send.</description></item>
/// </list>
///
/// <para>
/// The cost of getting it wrong is not a rejected request. LSP has a server exit <b>0</b> when a
/// <c>shutdown</c> preceded <c>exit</c> and <b>1</b> otherwise — so a server that refuses ours is correctly
/// reporting that we never shut it down, and a supervisor watching exit codes reads <em>every clean HexIDE
/// exit</em> as a crash. Verified end to end against an externally-authored server: omitting the member
/// changed its exit code from 1 to 0 (hexide-io/HexIDE#312).
/// </para>
///
/// <para>
/// <b>Why a raw framing server rather than a <c>JsonRpc</c> one.</b> A <c>[JsonRpcMethod]</c> handler is
/// told its arguments were bound; it cannot report whether <c>params</c> arrived as <c>[]</c>, as
/// <c>{}</c>, or not at all — which is the entire question. This reads the bytes.
/// </para>
/// </summary>
public class ShutdownWireShapeTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(30));

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Signals that the <c>exit</c> notification has actually been read off the wire.</summary>
    private readonly TaskCompletionSource _exitSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits for <c>exit</c> to arrive, so an assertion cannot race the server loop.</summary>
    private Task ExitArrivedAsync() => _exitSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>Every request and notification the client sent, in order, as raw JSON.</summary>
    private readonly List<JsonDocument> _received = [];

    private JsonElement FrameFor(string method) =>
        _received
            .Select(d => d.RootElement)
            .FirstOrDefault(e => e.TryGetProperty("method", out var m) && m.GetString() == method);

    private async Task<VBLspClient> ConnectedAsync()
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        // A minimal server, spoken by hand. It answers initialize and shutdown so the client's own
        // sequence completes, records everything, and interprets nothing.
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

                    // `exit` is a NOTIFICATION, so StopAsync returns as soon as the bytes are written and
                    // does not wait for us to read them. Without this signal the assertions race the
                    // server loop and fail intermittently on a correct implementation.
                    if (m.GetString() == "exit") _exitSeen.TrySetResult();

                    if (!doc.RootElement.TryGetProperty("id", out var id)) continue;   // notification

                    // Built by concatenation rather than an interpolated raw literal: the initialize
                    // result ends in three consecutive closing braces, which no $$"""...""" form accepts.
                    var head = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":";
                    var reply = m.GetString() == "initialize"
                        ? head + "{\"capabilities\":{},\"serverInfo\":"
                               + "{\"name\":\"wire-probe\",\"version\":\"1.0.0\"}}}"
                        : head + "null}";
                    await WriteFrameAsync(serverSide, reply, _cts.Token);
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

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        _disposables.Add(client);
        await client.StartAsync();
        return client;
    }

    [Fact]
    public async Task ShutdownCarriesNoParamsMemberAtAll()
    {
        // THE assertion. `"params":[]` — what the positional InvokeAsync overload sends — is a member,
        // and two of the three foreign servers this suite drives refuse it.
        var client = await ConnectedAsync();

        await client.StopAsync();

        var shutdown = FrameFor("shutdown");
        shutdown.ValueKind.Should().NotBe(JsonValueKind.Undefined,
            "StopAsync must send shutdown at all — a fix that simply stopped sending it would satisfy an "
          + "absence assertion while being a worse bug");

        shutdown.TryGetProperty("params", out var p).Should().BeFalse(
            $"LSP 3.17 defines shutdown as taking no parameters, and only an omitted member is accepted by "
          + $"every server measured — but the frame was {shutdown.GetRawText()}");
    }

    [Fact]
    public async Task ExitCarriesNoParamsMemberEither()
    {
        // `exit` had the same defect wearing different clothes: EmptyParams.Instance serialises to
        // `"params":{}`, which rumdl also refuses. A dropped `exit` is worse than a dropped `shutdown`,
        // because it is a notification — nothing reports the refusal and the server simply stays up.
        var client = await ConnectedAsync();

        await client.StopAsync();

        await ExitArrivedAsync();

        var exit = FrameFor("exit");
        exit.ValueKind.Should().NotBe(JsonValueKind.Undefined, "StopAsync must send exit after shutdown");
        exit.TryGetProperty("params", out _).Should().BeFalse(
            $"exit takes no parameters either — but the frame was {exit.GetRawText()}");
    }

    [Fact]
    public async Task ShutdownIsSentBeforeExit()
    {
        // The ordering is what the exit code means. A server exits 0 only if shutdown preceded exit, so
        // sending them the other way round would produce the same defect this change is fixing.
        var client = await ConnectedAsync();

        await client.StopAsync();

        await ExitArrivedAsync();

        List<string> methods;
        lock (_received)
        {
            methods = _received
                .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
                .Where(m => m is "shutdown" or "exit")
                .Select(m => m!)
                .ToList();
        }

        methods.Should().Equal(["shutdown", "exit"]);
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
