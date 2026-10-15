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
/// What a document's rename looks like on the wire: <c>didClose</c> under the old name, then
/// <c>didOpen</c> under the new one (#273 task 2.11).
///
/// <para>
/// <b>Why this reads the bytes when the sequence is already asserted elsewhere.</b> The tests in
/// <c>LspDocumentSessionTests</c> and <c>CodeEditorViewModelTests</c> assert it against an
/// <c>ILspClient</c> substitute, which proves the caller invoked its client in the right order with the
/// right arguments. That is a different claim from a server seeing two notifications. `CLAUDE.md` states
/// the general form: an assertion that our own call returned is not an assertion about the protocol, and
/// lifecycle is where this suite has been weakest. <c>ShutdownWireShapeTests</c> is the worked example, and
/// this is written in its style, over a server spoken by hand.
/// </para>
///
/// <para>
/// <b>The specific gap a substitute leaves here.</b> <c>CloseDocumentForRenameAsync</c> is gated on the
/// server's <c>textDocumentSync.openClose</c>, and it swallows its own exceptions. So it can be called,
/// return successfully, and put <b>nothing on the wire at all</b> — which every substitute-based assertion
/// records as a pass. The two halves of the guarantee therefore live in two places, deliberately: the
/// substitutes pin that the session closes the OLD name before opening the new one, and this file pins
/// what those two calls become in bytes.
/// </para>
/// </summary>
public class RenameWireShapeTests : IAsyncDisposable
{
    private const string Old = "untitled:Proj/Module1.bas";
    private const string New = "file:///c:/proj/TideTable.bas";

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

    // ── The server, spoken by hand ────────────────────────────────────────────────────────────────────

    /// <summary>Every frame the client sent, in arrival order.</summary>
    private readonly List<JsonDocument> _received = [];

    private readonly List<(string Method, string Uri, TaskCompletionSource Seen)> _waiters = [];

    private static string? MethodOf(JsonElement frame) =>
        frame.TryGetProperty("method", out var m) ? m.GetString() : null;

    /// <summary>The document a lifecycle notification is about, whichever shape its params take.</summary>
    private static string? UriOf(JsonElement frame) =>
        frame.TryGetProperty("params", out var p)
        && p.TryGetProperty("textDocument", out var d)
        && d.TryGetProperty("uri", out var u)
            ? u.GetString()
            : null;

    /// <summary>
    /// Every <c>didOpen</c>/<c>didClose</c>/<c>didChange</c> seen, as <c>method uri</c>. The assertion
    /// surface of this file, because the question is a sequence rather than any one frame.
    /// </summary>
    private List<string> Lifecycle()
    {
        lock (_received)
        {
            return _received
                .Select(d => d.RootElement)
                .Where(e => MethodOf(e) is "textDocument/didOpen"
                                        or "textDocument/didClose"
                                        or "textDocument/didChange")
                .Select(e => $"{MethodOf(e)!["textDocument/".Length..]} {UriOf(e)}")
                .ToList();
        }
    }

    private JsonElement FrameFor(string method, string uri)
    {
        lock (_received)
        {
            return _received.Select(d => d.RootElement)
                .LastOrDefault(e => MethodOf(e) == method && UriOf(e) == uri);
        }
    }

    /// <summary>
    /// Waits until a notification has actually been READ off the wire.
    /// </summary>
    /// <remarks>
    /// These are notifications, so the client's await returns once the bytes are written and does not wait
    /// for anyone to read them. Asserting straight after the call races the server loop and fails
    /// intermittently against a correct implementation — the trap <c>ShutdownWireShapeTests</c> records
    /// for <c>exit</c>.
    /// </remarks>
    private async Task ArrivedAsync(string method, string uri)
    {
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_received)
        {
            var already = _received.Select(d => d.RootElement)
                .Any(e => MethodOf(e) == method && UriOf(e) == uri);
            if (already) return;
            _waiters.Add((method, uri, seen));
        }

        try
        {
            await seen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            // Said rather than left as a bare timeout, because a frame this client declines to send is
            // sent silently -- the capability gate returns early and the caller's await completes
            // normally. So "nothing arrived" is the expected shape of that bug, and a test that reports it
            // as a hang sends the next reader looking for a deadlock instead.
            throw new TimeoutException(
                $"No {method} naming {uri} reached the wire within 10s. "
              + $"Lifecycle so far: [{string.Join(", ", Lifecycle())}]. "
              + "An open or close the server has not asked for is dropped by ServerCapabilities without "
              + "an error, so an absent frame is what a capability misread looks like.");
        }
    }

    private async Task<VBLspClient> ConnectedAsync()
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var json = await ReadFrameAsync(serverSide, _cts.Token);
                    if (json is null) return;

                    var doc = JsonDocument.Parse(json);
                    lock (_received)
                    {
                        _received.Add(doc);
                        var method = MethodOf(doc.RootElement);
                        var uri = UriOf(doc.RootElement);
                        foreach (var w in _waiters.Where(w => w.Method == method && w.Uri == uri).ToList())
                        {
                            w.Seen.TrySetResult();
                            _waiters.Remove(w);
                        }
                    }

                    if (!doc.RootElement.TryGetProperty("method", out var m)) continue;
                    if (!doc.RootElement.TryGetProperty("id", out var id)) continue;   // notification

                    // openClose plus Full change sync, so the client actually sends the lifecycle
                    // notifications this file is about. Deliberately NO diagnosticProvider: a pull would
                    // put request frames between the two notifications and answer none of the question.
                    var head = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":";
                    var reply = m.GetString() == "initialize"
                        ? head + "{\"capabilities\":{\"textDocumentSync\":{\"openClose\":true,\"change\":1}},"
                               + "\"serverInfo\":{\"name\":\"rename-probe\",\"version\":\"1.0.0\"}}}"
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

    /// <summary>A connected client with the document already open under its old name.</summary>
    private async Task<VBLspClient> OpenedAsync(string text = "Sub A\r\n")
    {
        var client = await ConnectedAsync();
        await client.OpenDocumentAsync(Old, text, isProjectMember: true, TestContext.Current.CancellationToken);
        await ArrivedAsync("textDocument/didOpen", Old);
        return client;
    }

    /// <summary>The two calls a rename makes, in the order <c>LspDocumentSession.RenameAsync</c> makes them.</summary>
    private async Task RenameAsync(VBLspClient client, string text = "Sub A\r\n")
    {
        await client.CloseDocumentForRenameAsync(Old, TestContext.Current.CancellationToken);
        await client.OpenDocumentAsync(New, text, isProjectMember: true, TestContext.Current.CancellationToken);
        await ArrivedAsync("textDocument/didOpen", New);
    }

    // ── The sequence ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TheOldNameIsClosedBeforeTheNewOneIsOpened()
    {
        // THE assertion, and note what it rules out beyond the ordering: that either notification was
        // never sent. Both are gated on the server's openClose capability and both swallow their own
        // exceptions, so "the call returned" is compatible with an empty wire.
        var client = await OpenedAsync();

        await RenameAsync(client);

        Lifecycle().Should().Equal(
            [$"didOpen {Old}", $"didClose {Old}", $"didOpen {New}"],
            "a server told to open the new name first holds both documents at once, and one never told to "
          + "close the old name keeps it open and goes on publishing diagnostics under a name the editor "
          + "has stopped answering to");
    }

    [Fact]
    public async Task TheCloseIsAnOrdinaryDidCloseCarryingNothingInvented()
    {
        // "For rename" is a distinction this client draws for its OWN diagnostic bookkeeping -- it raises
        // the clearing publication unconditionally, where an ordinary close waits for a push server to
        // clear the document itself. None of that is the server's business, and LSP has no way to say it:
        // a didClose takes a TextDocumentIdentifier and nothing else.
        var client = await OpenedAsync();

        await RenameAsync(client);

        var p = FrameFor("textDocument/didClose", Old).GetProperty("params");
        p.EnumerateObject().Select(x => x.Name).Should().Equal(["textDocument"]);
        p.GetProperty("textDocument").EnumerateObject().Select(x => x.Name).Should().Equal(["uri"]);
    }

    [Fact]
    public async Task TheReopenCarriesTheTextItWasGivenRatherThanTheTextItWasOpenedWith()
    {
        var client = await OpenedAsync("Sub A\r\n");

        await RenameAsync(client, "Sub A\r\nSub B\r\n");

        FrameFor("textDocument/didOpen", New).GetProperty("params").GetProperty("textDocument")
            .GetProperty("text").GetString()
            .Should().Be("Sub A\r\nSub B\r\n",
                "a rename is not a reload -- an edit made before it must survive it, and re-sending the "
              + "text the document was first opened with would silently undo whatever was typed since");
    }

    [Fact]
    public async Task TheReopenRestartsTheVersionAtOne()
    {
        // A didOpen is a document's first version by definition. Carrying the old counter forward would
        // hand a server a document it has never heard of at version 4 -- a desync it cannot detect, and
        // one that makes every subsequent change look like it arrived out of order.
        var client = await OpenedAsync();
        await client.ChangeDocumentAsync(Old, 2, "Sub A\r\nx\r\n", TestContext.Current.CancellationToken);
        await client.ChangeDocumentAsync(Old, 3, "Sub A\r\nxy\r\n", TestContext.Current.CancellationToken);

        await RenameAsync(client, "Sub A\r\nxy\r\n");

        FrameFor("textDocument/didOpen", New).GetProperty("params").GetProperty("textDocument")
            .GetProperty("version").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task NothingIsSaidAboutTheOldNameAfterItIsClosed()
    {
        // The close is what makes the old name safe to forget. A change routed to it afterwards -- by a
        // debounce that outlived the rename, say -- names a document the server has been told is gone, and
        // a conformant server is entitled to treat that as a protocol error.
        var client = await OpenedAsync();

        await RenameAsync(client);
        await client.ChangeDocumentAsync(New, 2, "Sub A\r\nSub B\r\n", TestContext.Current.CancellationToken);
        await ArrivedAsync("textDocument/didChange", New);

        Lifecycle().SkipWhile(l => l != $"didClose {Old}").Skip(1)
            .Should().NotContain(l => l.EndsWith(Old));
    }

    // ---- LSP framing, by hand ------------------------------------------------------------------
    //
    // Duplicated from ShutdownWireShapeTests rather than shared. Both files exist to read what actually
    // crossed the wire, and a helper they both depend on is a place for one bug to make both of them agree
    // with it -- which is the failure mode this whole style of test is placed against.

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
        foreach (var line in Encoding.ASCII.GetString([.. header])
                     .Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
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
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(
            $"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }
}
