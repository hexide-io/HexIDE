using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// <c>textDocument/codeLens</c> and <c>workspace/executeCommand</c> — the pair that lets a server offer an
/// action against a range and have the client invoke it. Implemented together because neither is useful
/// alone: a lens carries a command, and a command with nothing to trigger it is unreachable.
///
/// <para>
/// Driven against a stub server that answers for real rather than a substituted client, because two of the
/// three things worth pinning here — whether a resolve round trip happened, and what actually arrived in
/// <c>params</c> — are invisible to a mock that only records the call.
/// </para>
///
/// <para>
/// <b>Why a bound <c>JsonElement</c> handler suffices where <c>shutdown</c> needed raw framing.</b>
/// <c>ShutdownWireShapeTests</c> reads bytes because its question is whether <c>params</c> exists at all,
/// which a handler told "your argument was bound" cannot answer. The question here is about a property
/// <em>inside</em> <c>params</c>, and a handler taking the params object sees that exactly.
/// </para>
/// </summary>
public class CodeLensAndCommandTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private async Task<VBLspClient> ConnectedToAsync(StubServer server)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        // IsRunning is `transport.IsAlive && initialized`; a substitute reports false by default, and every
        // request below is gated on it.
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        _disposables.Add(client);
        await client.StartAsync(TestContext.Current.CancellationToken);
        return client;
    }

    // ── codeLens ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUnresolvedLensIsResolvedBeforeItReachesTheCaller()
    {
        // The point of resolving inside the client. A lens with no command is one the user can see and
        // cannot click; the caller cannot fix that itself, because once lenses from several servers are
        // gathered into one list nothing records which connection produced which.
        var server = new StubServer(
            capabilities: """{"codeLensProvider":{"resolveProvider":true}}""",
            lenses: """[{"range":{"start":{"line":3,"character":0},"end":{"line":3,"character":9}}}]""");

        var sut = await ConnectedToAsync(server);

        var lenses = await sut.RequestCodeLensesAsync("vb6://module/M", TestContext.Current.CancellationToken);

        lenses.Should().ContainSingle();
        lenses[0].Command.Should().NotBeNull("the lens arrived unresolved and must not be handed on that way");
        lenses[0].Command!.CommandName.Should().Be("test.run");
        lenses[0].Command!.Title.Should().Be("Run test");
        server.ResolveCalls.Should().Be(1);
    }

    [Fact]
    public async Task ALensThatArrivesCompleteIsNotResolvedAgain()
    {
        // Guards the condition, not the capability. An implementation that resolved unconditionally would
        // pass the test above and double every round trip here, which nothing else would notice.
        var server = new StubServer(
            capabilities: """{"codeLensProvider":{"resolveProvider":true}}""",
            lenses: """
                [{"range":{"start":{"line":1,"character":0},"end":{"line":1,"character":4}},
                  "command":{"title":"Already here","command":"test.run"}}]
                """);

        var sut = await ConnectedToAsync(server);

        var lenses = await sut.RequestCodeLensesAsync("vb6://module/M", TestContext.Current.CancellationToken);

        lenses.Should().ContainSingle();
        lenses[0].Command!.Title.Should().Be("Already here");
        server.ResolveCalls.Should().Be(0, "the command was already present, so there was nothing to ask for");
    }

    [Fact]
    public async Task NoResolveIsAttemptedWhenTheServerOffersNone()
    {
        // `codeLensProvider: true` is legal and means lenses arrive complete. Asking such a server to
        // resolve would be sending a method it never advertised.
        var server = new StubServer(
            capabilities: """{"codeLensProvider":true}""",
            lenses: """[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}}]""");

        var sut = await ConnectedToAsync(server);

        var lenses = await sut.RequestCodeLensesAsync("vb6://module/M", TestContext.Current.CancellationToken);

        lenses.Should().ContainSingle();
        lenses[0].Command.Should().BeNull();
        server.ResolveCalls.Should().Be(0);
    }

    [Fact]
    public async Task NothingIsAskedOfAServerThatDoesNotAdvertiseCodeLens()
    {
        var server = new StubServer(capabilities: """{"hoverProvider":true}""", lenses: "[]");

        var sut = await ConnectedToAsync(server);

        var lenses = await sut.RequestCodeLensesAsync("vb6://module/M", TestContext.Current.CancellationToken);

        lenses.Should().BeEmpty();
        server.CodeLensCalls.Should().Be(0, "the capability gate is what stops us sending an unsupported request");
    }

    [Fact]
    public async Task AResolveHandsBackTheServersOwnDataAndInventsNothing()
    {
        // `data` is the server's private handle for the lens and the one field it is entitled to receive
        // verbatim. Equally: a `command` it did not send must not come back as an explicit null, which is
        // what a record serialised without an omit-when-null condition would produce.
        var server = new StubServer(
            capabilities: """{"codeLensProvider":{"resolveProvider":true}}""",
            lenses: """
                [{"range":{"start":{"line":7,"character":0},"end":{"line":7,"character":2}},
                  "data":{"id":42,"kind":"test"}}]
                """);

        var sut = await ConnectedToAsync(server);

        await sut.RequestCodeLensesAsync("vb6://module/M", TestContext.Current.CancellationToken);

        var sent = server.LastResolveParams;
        sent.HasValue.Should().BeTrue();
        sent!.Value.TryGetProperty("data", out var data).Should().BeTrue("the server's own handle must survive");
        data.GetProperty("id").GetInt32().Should().Be(42);
        data.GetProperty("kind").GetString().Should().Be("test");
        sent.Value.TryGetProperty("command", out _).Should().BeFalse(
            "the lens carried no command, so the echo must not add one as an explicit null — "
          + $"the frame was {sent.Value.GetRawText()}");
    }

    // ── executeCommand ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ADeclaredCommandReachesTheServerWithItsArguments()
    {
        var server = new StubServer(
            capabilities: """{"executeCommandProvider":{"commands":["test.run","test.debug"]}}""",
            lenses: "[]");

        var sut = await ConnectedToAsync(server);

        var args = JsonDocument.Parse("""["Module1", 7]""").RootElement.EnumerateArray().ToArray();
        var result = await sut.ExecuteCommandAsync("test.run", args, TestContext.Current.CancellationToken);

        server.LastCommand.Should().Be("test.run");
        server.LastCommandParams!.Value.GetProperty("arguments")[0].GetString().Should().Be("Module1");
        server.LastCommandParams!.Value.GetProperty("arguments")[1].GetInt32().Should().Be(7);
        result!.Value.GetProperty("ran").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ACommandTheServerDidNotDeclareIsNotSent()
    {
        // THE routing assertion. A command belongs to whichever server named it; sending one to a server
        // that did not would not fail quietly — it would run something, or run the wrong thing.
        var server = new StubServer(
            capabilities: """{"executeCommandProvider":{"commands":["test.run"]}}""",
            lenses: "[]");

        var sut = await ConnectedToAsync(server);

        var result = await sut.ExecuteCommandAsync(
            "somebody.elses.command", null, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        server.LastCommand.Should().BeNull("a command this server never declared must not reach it at all");
    }

    [Fact]
    public async Task AProviderThatNamesNoCommandsOwnsNone()
    {
        // `commands` is required by the specification, so this shape is malformed — and the safe reading of
        // a malformed provider is that it declares nothing, not that it declares everything.
        var server = new StubServer(
            capabilities: """{"executeCommandProvider":{"workDoneProgress":false}}""",
            lenses: "[]");

        var sut = await ConnectedToAsync(server);

        var result = await sut.ExecuteCommandAsync("test.run", null, TestContext.Current.CancellationToken);

        result.Should().BeNull();
        server.LastCommand.Should().BeNull();
    }

    [Fact]
    public async Task ACommandWithNoArgumentsOmitsTheMemberRatherThanSendingNull()
    {
        // The #312 lesson applied before it can cost anything: where "absent" and "null" differ only in how
        // strict the reader is, send the one nothing can object to.
        var server = new StubServer(
            capabilities: """{"executeCommandProvider":{"commands":["test.runAll"]}}""",
            lenses: "[]");

        var sut = await ConnectedToAsync(server);

        await sut.ExecuteCommandAsync("test.runAll", null, TestContext.Current.CancellationToken);

        var sent = server.LastCommandParams;
        sent.HasValue.Should().BeTrue("the command was declared, so it must have been sent");
        sent!.Value.TryGetProperty("arguments", out _).Should().BeFalse(
            "arguments is optional, and an omitted member is valid to every reader while an explicit null "
          + $"is only probably-tolerated — the frame was {sent.Value.GetRawText()}");
    }

    /// <summary>
    /// A server that answers for real: it initializes with the given capabilities, returns the given lenses,
    /// resolves one by filling in a command, and records what it was actually sent.
    /// </summary>
    private sealed class StubServer(string capabilities, string lenses)
    {
        public int CodeLensCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public JsonElement? LastResolveParams { get; private set; }
        public string? LastCommand { get; private set; }
        public JsonElement? LastCommandParams { get; private set; }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse($$"""{"capabilities":{{capabilities}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/codeLens", UseSingleObjectParameterDeserialization = true)]
        public JsonElement CodeLens(JsonElement _)
        {
            CodeLensCalls++;
            return JsonDocument.Parse(lenses).RootElement.Clone();
        }

        [JsonRpcMethod("codeLens/resolve", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Resolve(JsonElement lens)
        {
            ResolveCalls++;
            LastResolveParams = lens.Clone();

            // Echo the range back with a command attached — what a real server does, and what makes the
            // difference between resolved and unresolved observable to the caller.
            // Concatenated rather than an interpolated raw literal: this ends in two consecutive closing
            // braces, which no $$"""...""" form accepts as content.
            var range = lens.GetProperty("range").GetRawText();
            return JsonDocument.Parse(
                "{\"range\":" + range + ",\"command\":{\"title\":\"Run test\",\"command\":\"test.run\"}}")
                .RootElement.Clone();
        }

        [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Execute(JsonElement p)
        {
            LastCommandParams = p.Clone();
            LastCommand = p.GetProperty("command").GetString();
            return JsonDocument.Parse("""{"ran":true}""").RootElement.Clone();
        }
    }
}
