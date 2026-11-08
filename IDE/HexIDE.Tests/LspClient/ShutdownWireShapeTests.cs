using System.Globalization;
using System.IO.Pipes;
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
/// What the lifecycle messages look like on the wire.
///
/// <para>
/// LSP 3.17 defines shutdown and exit as taking <b>no parameters</b>, and the four ways of expressing "nothing" are not
/// interchangeable to a real server. Measured against foreign servers this suite drives and an
/// externally-authored one (hexide-io/HexIDE#312, #368):
/// </para>
///
/// <list type="table">
/// <item><description><c>"params":[]</c> — REJECTED by rumdl (tower-lsp) and by OmniSharp-based servers.
///   This is what <c>JsonRpc.InvokeAsync(name)</c> sends, because it is the <b>positional</b>
///   overload.</description></item>
/// <item><description><c>"params":{}</c> — REJECTED by rumdl. This is what <c>EmptyParams.Instance</c>
///   sends.</description></item>
/// <item><description><c>"params":null</c> — REJECTED by rumdl 0.2.64 with <c>-32602</c>,
///   <c>Unexpected params: null</c>. A null argument object in StreamJsonRpc's parameter-object overload
///   omits the member; it does not send JSON null.</description></item>
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
/// <c>{}</c>, as <c>null</c>, or not at all — which is the entire question. This reads the bytes.
/// The existing absence assertions reject all three present spellings; accepting null or empty
/// parameters instead would weaken the guard.
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

    // Under the same lock the server loop appends under. ExitArrivedAsync only says `exit` was read; the loop goes on
    // running after it, and enumerating the list while it appends threw "Collection was modified" (#565).
    private JsonElement FrameFor(string method)
    {
        lock (_received)
        {
            return _received
                .Select(d => d.RootElement)
                .FirstOrDefault(e => e.TryGetProperty("method", out var m) && m.GetString() == method);
        }
    }

    private ILspTransport? _transport;
    private Task? _serverLoop;
    private Task? _starting;
    private readonly TaskCompletionSource _initializeSeen = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task<VBLspClient> ConnectedAsync(
        bool answerShutdown = true, bool answerInitialize = true, bool leaveServerRunning = false,
        Func<string>? locale = null, bool callHierarchy = true, string? hierarchyResult = null, bool hierarchyError = false, string rangeProvider = "true", bool configured = false, string? options = null)
    {
        var (clientSide, duplexServer) = FullDuplexStream.CreatePair();
        // Short enough for macOS, whose temp directory leaves 43 characters for a pipe name (PipeNameLimit).
        var pipeName = "hx-init-" + Guid.NewGuid().ToString("N")[..12];
        var pipe = configured ? new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous) : null;
        Stream serverSide = pipe ?? duplexServer;
        _disposables.Add(clientSide);
        _disposables.Add(duplexServer);
        if (configured) _disposables.Add(serverSide);

        // A minimal server, spoken by hand. It answers initialize and shutdown so the client's own
        // sequence completes, records everything, and interprets nothing.
        _serverLoop = Task.Run(async () =>
        {
            try
            {
                if (pipe is not null) await pipe.WaitForConnectionAsync(_cts.Token);
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

                    if (m.GetString() == "shutdown" && !answerShutdown) continue;
                    if (m.GetString() == "initialize")
                    {
                        _initializeSeen.TrySetResult();
                        if (!answerInitialize) continue;
                    }

                    // Built by concatenation rather than an interpolated raw literal: the initialize
                    // result ends in three consecutive closing braces, which no $$"""...""" form accepts.
                    var head = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":";
                    var reply = m.GetString() == "initialize"
                        ? head + "{\"capabilities\":{\"callHierarchyProvider\":" + (callHierarchy ? "{}" : "false") + ",\"documentFormattingProvider\":true,\"documentRangeFormattingProvider\":" + rangeProvider + "},\"serverInfo\":"
                               + "{\"name\":\"wire-probe\",\"version\":\"1.0.0\"}}}"
                        : head + "null}";
                    if (m.GetString() is "textDocument/prepareCallHierarchy" or
                        "callHierarchy/incomingCalls" or "callHierarchy/outgoingCalls")
                    {
                        if (hierarchyError)
                            reply = "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText()
                                + ",\"error\":{\"code\":-32603,\"message\":\"unavailable\"}}";
                        else if (hierarchyResult is not null)
                            reply = head + hierarchyResult + "}";
                    }
                    await WriteFrameAsync(serverSide, reply, _cts.Token);
                }
            }
            catch (OperationCanceledException) { /* the test finished */ }
            catch (IOException) { /* the client hung up */ }
            catch (ObjectDisposedException) { /* the test finished */ }
        }, _cts.Token);

        var transport = _transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.LeaveServerRunning.Returns(leaveServerRunning);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var optionsMember = options is null ? "" : ",\"initializationOptions\":" + options;
        var client = configured
            ? (VBLspClient)new LanguageServerRegistrationFactory(Substitute.For<ILoggerFactory>())
                .Create(JsonSerializer.Deserialize<LanguageServerConfigFile>($$"""
                    {"version":1,"servers":[{"id":"probe","languageId":"vb6","extensions":[".bas"],
                    "transport":"pipe","pipeName":"{{pipeName}}"{{optionsMember}}}]}
                    """)!.Servers!)
                .Single().CreateClient()
            : new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6", locale: locale);
        _disposables.Add(client);
        _starting = client.StartAsync();
        if (answerInitialize) await _starting;
        else await _initializeSeen.Task.WaitAsync(TimeSpan.FromSeconds(10), _cts.Token);
        return client;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    public async Task InitializeOptionsAreAbsentWhenNotConfigured(string? options)
    {
        await ConnectedAsync(configured: true, options: options);
        FrameFor("initialize").GetProperty("params").TryGetProperty("initializationOptions", out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Language\":\"vb6\",\"nested\":{\"flags\":[true,2,null,\"opaque\"]}}")]
    public async Task InitializeOptionsPassThroughRegistrationUnchanged(string options)
    {
        await ConnectedAsync(configured: true, options: options);
        var parameters = FrameFor("initialize").GetProperty("params");
        parameters.TryGetProperty("initializationOptions", out var sent).Should().BeTrue();
        JsonElement.DeepEquals(sent, JsonDocument.Parse(options).RootElement).Should().BeTrue();
    }

    [Fact]
    public async Task CallHierarchyCapabilityIsDeclared()
    {
        await ConnectedAsync();
        FrameFor("initialize").GetProperty("params").GetProperty("capabilities")
            .GetProperty("textDocument").TryGetProperty("callHierarchy", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PrepareCallHierarchyCarriesCaret()
    {
        var client = await ConnectedAsync();
        (await client.PrepareCallHierarchyAsync("file:///M.bas", new Position(7, 9),
            TestContext.Current.CancellationToken)).Should().BeEmpty();
        var parameters = FrameFor("textDocument/prepareCallHierarchy").GetProperty("params");
        parameters.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///M.bas");
        parameters.GetProperty("position").GetProperty("line").GetInt32().Should().Be(7);
        parameters.GetProperty("position").GetProperty("character").GetInt32().Should().Be(9);
    }

    [Theory]
    [InlineData("Incoming", "incomingCalls", false)]
    [InlineData("Outgoing", "outgoingCalls", false)]
    [InlineData("Incoming", "incomingCalls", true)]
    [InlineData("Outgoing", "outgoingCalls", true)]
    [InlineData("Incoming", "incomingCalls", null)]
    [InlineData("Outgoing", "outgoingCalls", null)]
    public async Task CallHierarchyExpansionPreservesOpaqueItem(string direction, string wireMethod, bool? nullData)
    {
        var client = await ConnectedAsync();
        const string json = """
            {"name":"Foo","kind":12,"uri":"file:///M.bas",
             "range":{"start":{"line":1,"character":0},"end":{"line":4,"character":7}},
             "selectionRange":{"start":{"line":1,"character":4},"end":{"line":1,"character":7}},
             "data":{"token":[1,"opaque"]},"tags":[1],"detail":"Module1"}
            """;
        var payload = nullData switch
        {
            true => json.Replace("{\"token\":[1,\"opaque\"]}", "null"),
            null => json.Replace("\"data\":{\"token\":[1,\"opaque\"]},", ""),
            _ => json
        };
        var item = JsonSerializer.Deserialize<CallHierarchyItem>(payload)!;
        if (direction == "Incoming")
            (await client.RequestIncomingCallsAsync(item, TestContext.Current.CancellationToken)).Should().BeEmpty();
        else
            (await client.RequestOutgoingCallsAsync(item, TestContext.Current.CancellationToken)).Should().BeEmpty();
        var sent = FrameFor("callHierarchy/" + wireMethod).GetProperty("params").GetProperty("item");
        JsonElement.DeepEquals(sent, JsonDocument.Parse(payload).RootElement).Should().BeTrue();
    }

    [Fact]
    public async Task CallHierarchyWithoutProviderSendsNoRequest()
    {
        var client = await ConnectedAsync(callHierarchy: false);
        (await client.PrepareCallHierarchyAsync("file:///M.bas", new Position(0, 0),
            TestContext.Current.CancellationToken)).Should().BeEmpty();
        await client.StopAsync();
        await ExitArrivedAsync();
        FrameFor("textDocument/prepareCallHierarchy").ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    private const string HierarchyItemJson = """
        {"name":"Foo","kind":12,"uri":"file:///M.bas",
         "range":{"start":{"line":1,"character":0},"end":{"line":4,"character":7}},
         "selectionRange":{"start":{"line":1,"character":4},"end":{"line":1,"character":7}},
         "data":{"token":[1,"opaque"]}}
        """;

    [Fact]
    public async Task CallHierarchyPrepareReadsServerItems()
    {
        var client = await ConnectedAsync(hierarchyResult: "[" + HierarchyItemJson + "]");
        var items = await client.PrepareCallHierarchyAsync("file:///M.bas", new Position(1, 4),
            TestContext.Current.CancellationToken);
        var item = items.Should().ContainSingle().Which;
        item.Name.Should().Be("Foo");
        item.SelectionRange.Start.Should().Be(new Position(1, 4));
        item.Data.GetProperty("token")[1].GetString().Should().Be("opaque");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallHierarchyExpansionReadsServerItemsAndCallSites(bool incoming)
    {
        var relation = incoming ? "from" : "to";
        var reply = "[{\"" + relation + "\":" + HierarchyItemJson + """
            ,"fromRanges":[{"start":{"line":2,"character":3},"end":{"line":2,"character":6}}]}]
            """;
        var client = await ConnectedAsync(hierarchyResult: reply);
        var root = JsonSerializer.Deserialize<CallHierarchyItem>(HierarchyItemJson)!;
        CallHierarchyItem item;
        HexIDE.Lsp.Messages.Range[] ranges;
        if (incoming)
        {
            var call = (await client.RequestIncomingCallsAsync(root, TestContext.Current.CancellationToken))
                .Should().ContainSingle().Which;
            item = call.From;
            ranges = call.FromRanges;
        }
        else
        {
            var call = (await client.RequestOutgoingCallsAsync(root, TestContext.Current.CancellationToken))
                .Should().ContainSingle().Which;
            item = call.To;
            ranges = call.FromRanges;
        }
        item.Name.Should().Be("Foo");
        item.Data.GetProperty("token")[1].GetString().Should().Be("opaque");
        ranges.Should().ContainSingle().Which.Should().Be(new HexIDE.Lsp.Messages.Range(
            new Position(2, 3), new Position(2, 6)));
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("incoming")]
    [InlineData("outgoing")]
    public async Task CallHierarchyErrorsPropagateToCaller(string request)
    {
        var client = await ConnectedAsync(hierarchyError: true);
        var root = JsonSerializer.Deserialize<CallHierarchyItem>(HierarchyItemJson)!;
        Func<Task> action = request switch
        {
            "prepare" => () => client.PrepareCallHierarchyAsync(root.Uri, new Position(1, 4),
                TestContext.Current.CancellationToken),
            "incoming" => () => client.RequestIncomingCallsAsync(root, TestContext.Current.CancellationToken),
            _ => () => client.RequestOutgoingCallsAsync(root, TestContext.Current.CancellationToken)
        };
        (await action.Should().ThrowAsync<RemoteInvocationException>()).Which.Message.Should().Contain("unavailable");
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(8, true)]
    [InlineData(8, false)]
    public async Task FormattingCarriesTheRequestedIndentation(int tabWidth, bool insertSpaces)
    {
        var client = await ConnectedAsync();

        await client.RequestFormattingAsync("file:///M.bas", new FormattingOptions(tabWidth, insertSpaces),
            TestContext.Current.CancellationToken);

        var options = FrameFor("textDocument/formatting").GetProperty("params").GetProperty("options");
        options.GetProperty("tabSize").GetInt32().Should().Be(tabWidth);
        options.GetProperty("insertSpaces").GetBoolean().Should().Be(insertSpaces);
    }

    [Fact]
    public async Task RangeFormattingIsDeclaredAndCarriesSelectionAndOptions()
    {
        var client = await ConnectedAsync();
        FrameFor("initialize").GetProperty("params").GetProperty("capabilities")
            .GetProperty("textDocument").TryGetProperty("rangeFormatting", out _).Should().BeTrue();
        var method = typeof(ILspClient).GetMethod("RequestRangeFormattingAsync");
        method.Should().NotBeNull();
        var range = new HexIDE.Lsp.Messages.Range(new Position(2, 3), new Position(4, 5));
        await (Task<TextEdit[]>)method!.Invoke(client,
            ["file:///M.bas", range, new FormattingOptions(8, true), TestContext.Current.CancellationToken])!;
        var p = FrameFor("textDocument/rangeFormatting").GetProperty("params");
        p.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///M.bas");
        p.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32().Should().Be(2);
        p.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32().Should().Be(3);
        p.GetProperty("range").GetProperty("end").GetProperty("line").GetInt32().Should().Be(4);
        p.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32().Should().Be(5);
        p.GetProperty("options").GetProperty("tabSize").GetInt32().Should().Be(8);
        p.GetProperty("options").GetProperty("insertSpaces").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("null")]
    public async Task RangeFormattingIsNotSentWithoutServerSupport(string provider)
    {
        var client = await ConnectedAsync(rangeProvider: provider);
        var method = typeof(ILspClient).GetMethod("RequestRangeFormattingAsync");
        method.Should().NotBeNull();
        var range = new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(1, 0));
        var edits = await (Task<TextEdit[]>)method!.Invoke(client,
            ["file:///M.bas", range, new FormattingOptions(4, true), TestContext.Current.CancellationToken])!;
        edits.Should().BeEmpty();
        await client.StopAsync();
        await ExitArrivedAsync();
        FrameFor("textDocument/rangeFormatting").ValueKind.Should().Be(JsonValueKind.Undefined);
    }

    [AvaloniaTheory]
    [InlineData("en-GB", "en-GB")]
    [InlineData("fr", "fr")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("system", "en")]
    [InlineData("pseudo", "en")]
    [InlineData("pseudo-rtl", "en")]
    public async Task InitializeCarriesResolvedUiLocale(string language, string expected)
    {
        var previous = HexIDE.Localization.LocalizationService.OsUICulture;
        try
        {
            HexIDE.Localization.LocalizationService.OsUICulture = CultureInfo.InvariantCulture;
            var localization = new HexIDE.Localization.LocalizationService();
            localization.Apply(language);
            await ConnectedAsync(locale: () => localization.ResolvedLanguage);

            FrameFor("initialize").GetProperty("params").GetProperty("locale")
                .GetString().Should().Be(expected);
        }
        finally
        {
            HexIDE.Localization.LocalizationService.OsUICulture = previous;
        }
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

    [Fact]
    public async Task AnUnansweredShutdownStillSendsExitAndDisposesTheTransport()
    {
        var client = await ConnectedAsync(answerShutdown: false);

        await client.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), _cts.Token);
        await ExitArrivedAsync();

        FrameFor("shutdown").TryGetProperty("params", out _).Should().BeFalse();
        var exit = FrameFor("exit");
        exit.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        exit.TryGetProperty("params", out _).Should().BeFalse();
        await _transport!.Received(1).DisposeAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task StopOnlyClosesTheConnectionWhenShutdownIsNotAllowed(
        bool answerInitialize, bool leaveServerRunning)
    {
        var client = await ConnectedAsync(answerInitialize: answerInitialize,
            leaveServerRunning: leaveServerRunning);

        await client.StopAsync();

        // Drain the server loop through EOF before checking absence on the wire.
        await _serverLoop!;
        await _starting!;
        FrameFor("shutdown").ValueKind.Should().Be(JsonValueKind.Undefined);
        FrameFor("exit").ValueKind.Should().Be(JsonValueKind.Undefined);
        await _transport!.Received(1).DisposeAsync();
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
