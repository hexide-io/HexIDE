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
        server.Rpc = serverRpc;
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

    [Theory]
    [InlineData("{\"inlayHintProvider\":true}")]
    [InlineData("{\"inlayHintProvider\":{\"resolveProvider\":false}}")]
    public async Task InlayHintsSendTheDocumentAndRange(string capabilities)
    {
        var server = new StubServer(capabilities, "[]");
        var client = await ConnectedToAsync(server);
        var range = new HexIDE.Lsp.Messages.Range(new Position(2, 0), new Position(8, 7));
        var hints = await client.RequestInlayHintsAsync("file:///test.bas", range, TestContext.Current.CancellationToken);
        hints.Should().ContainSingle();
        hints[0].Text.Should().Be("' Sub Foo");
        server.LastInitializeParams!.Value.GetProperty("capabilities").GetProperty("textDocument")
            .GetProperty("inlayHint").ValueKind.Should().Be(JsonValueKind.Object);
        server.LastInlayParams!.Value.GetProperty("textDocument").GetProperty("uri").GetString()
            .Should().Be("file:///test.bas");
        server.LastInlayParams.Value.GetProperty("range").GetProperty("end").GetProperty("line").GetInt32()
            .Should().Be(8);
        foreach (var unsupported in new[] { "{}", "{\"inlayHintProvider\":false}" })
        {
            var silent = new StubServer(unsupported, "[]");
            var gated = await ConnectedToAsync(silent);
            (await gated.RequestInlayHintsAsync("file:///test.bas", range,
                TestContext.Current.CancellationToken)).Should().BeEmpty();
            silent.LastInlayParams.Should().BeNull();
        }
    }

    [Theory]
    [InlineData("{\"referencesProvider\":true}")]
    [InlineData("{\"referencesProvider\":{}}")]
    public async Task ReferencesSendPositionAndIncludeDeclaration(string capabilities)
    {
        var server = new StubServer(capabilities, "[]");
        ILspClient client = await ConnectedToAsync(server);
        var locations = await client.RequestReferencesAsync("file:///test.bas", new Position(3, 7),
            TestContext.Current.CancellationToken);
        locations.Should().ContainSingle();
        var sent = server.LastReferenceParams!.Value;
        sent.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///test.bas");
        sent.GetProperty("position").GetProperty("line").GetInt32().Should().Be(3);
        sent.GetProperty("position").GetProperty("character").GetInt32().Should().Be(7);
        sent.GetProperty("context").GetProperty("includeDeclaration").GetBoolean().Should().BeTrue();
        foreach (var unsupported in new[] { "{}", "{\"referencesProvider\":false}" })
        {
            var silent = new StubServer(unsupported, "[]");
            ILspClient gated = await ConnectedToAsync(silent);
            (await gated.RequestReferencesAsync("file:///test.bas", new Position(3, 7),
                TestContext.Current.CancellationToken)).Should().BeEmpty();
            silent.LastReferenceParams.Should().BeNull();
        }
    }

    [Fact]
    public async Task ReferenceServerErrorsReachTheCaller()
    {
        var server = new StubServer("""{"referencesProvider":true}""", "[]") { FailReferences = true };
        ILspClient client = await ConnectedToAsync(server);
        var request = () => client.RequestReferencesAsync("file:///test.bas", new Position(0, 0),
            TestContext.Current.CancellationToken);
        await request.Should().ThrowAsync<RemoteInvocationException>().WithMessage("*reference failure*");
    }

    [Fact]
    public async Task InitializeDeclaresWorkspaceCodeLensRefreshSupport()
    {
        var server = new StubServer("{}", "[]");
        await ConnectedToAsync(server);
        var workspace = server.LastInitializeParams!.Value.GetProperty("capabilities").GetProperty("workspace");
        workspace.GetProperty("codeLens").GetProperty("refreshSupport").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ParameterlessCodeLensRefreshAnswersNullAndInvalidatesConsumers()
    {
        var server = new StubServer("{}", "[]");
        var client = await ConnectedToAsync(server);
        var invalidations = 0;
        object? origin = null;
        client.CodeLensRefreshRequested += (sender, _) => { origin = sender; invalidations++; };
        var result = await server.Rpc!.InvokeAsync<JsonElement?>("workspace/codeLens/refresh");
        result.Should().BeNull();
        invalidations.Should().Be(1);
        origin.Should().BeSameAs(client);
    }

    // ── codeLens ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchingLensesDoesNotResolveThem()
    {
        var server = new StubServer(
            capabilities: """{"codeLensProvider":{"resolveProvider":true}}""",
            lenses: """[{"range":{"start":{"line":3,"character":0},"end":{"line":3,"character":9}}}]""");
        var sut = await ConnectedToAsync(server);

        var lenses = await sut.RequestCodeLensesAsync("vb6://module/M", TestContext.Current.CancellationToken);

        lenses.Should().ContainSingle();
        lenses[0].Command.Should().BeNull();
        server.ResolveCalls.Should().Be(0, "visibility is not known when the document's lenses are fetched");
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
        var resolved = await sut.ResolveCodeLensAsync(lenses[0], TestContext.Current.CancellationToken);
        resolved.Should().BeSameAs(lenses[0]);
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
        (await sut.ResolveCodeLensAsync(lenses[0], TestContext.Current.CancellationToken)).Should().BeNull();
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

        var lenses = await sut.RequestCodeLensesAsync("vb6://module/M", TestContext.Current.CancellationToken);
        await sut.ResolveCodeLensAsync(lenses[0], TestContext.Current.CancellationToken);

        var sent = server.LastResolveParams;
        sent.HasValue.Should().BeTrue();
        sent!.Value.TryGetProperty("data", out var data).Should().BeTrue("the server's own handle must survive");
        data.GetProperty("id").GetInt32().Should().Be(42);
        data.GetProperty("kind").GetString().Should().Be("test");
        sent.Value.TryGetProperty("command", out _).Should().BeFalse(
            "the lens carried no command, so the echo must not add one as an explicit null — "
          + $"the frame was {sent.Value.GetRawText()}");
    }

    [Fact]
    public async Task OnlyRequestedLensesAreResolved()
    {
        var lensesJson = JsonSerializer.Serialize(Enumerable.Range(0, 50).Select(i =>
            new CodeLens(new HexIDE.Lsp.Messages.Range(new Position(i, 0), new Position(i, 1)))));
        var server = new StubServer("""{"codeLensProvider":{"resolveProvider":true}}""", lensesJson);
        var sut = await ConnectedToAsync(server);
        var lenses = await sut.RequestCodeLensesAsync("file:///test.bas", TestContext.Current.CancellationToken);

        foreach (var lens in lenses.Take(10))
        {
            var resolved = await sut.ResolveCodeLensAsync(lens, TestContext.Current.CancellationToken);
            resolved!.Command!.Title.Should().Be("Run test");
        }
        server.ResolveCalls.Should().Be(10);
        server.LastResolveParams!.Value.GetProperty("range").GetProperty("start").GetProperty("line")
            .GetInt32().Should().Be(9);
    }

    [Fact]
    public async Task CancelledResolutionDoesNotReachTheServer()
    {
        var server = new StubServer("""{"codeLensProvider":{"resolveProvider":true}}""",
            """[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}}]""");
        var sut = await ConnectedToAsync(server);
        var lenses = await sut.RequestCodeLensesAsync("file:///test.bas", TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        (await sut.ResolveCodeLensAsync(lenses[0], cancelled.Token)).Should().BeNull();
        server.ResolveCalls.Should().Be(0);
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
        public JsonRpc? Rpc { get; set; }
        public JsonElement? LastInlayParams { get; private set; }
        public JsonElement? LastInitializeParams { get; private set; }

        [JsonRpcMethod("textDocument/inlayHint", UseSingleObjectParameterDeserialization = true)]
        public JsonElement InlayHint(JsonElement p)
        {
            LastInlayParams = p.Clone();
            return JsonDocument.Parse("""[{"position":{"line":8,"character":7},"label":"' Sub Foo"}]""")
                .RootElement.Clone();
        }

        public bool FailReferences { get; init; }
        public JsonElement? LastReferenceParams { get; private set; }

        [JsonRpcMethod("textDocument/references", UseSingleObjectParameterDeserialization = true)]
        public JsonElement References(JsonElement p)
        {
            LastReferenceParams = p.Clone();
            if (FailReferences) throw new InvalidOperationException("reference failure");
            return JsonDocument.Parse("""[{"uri":"file:///test.bas","range":{"start":{"line":3,"character":7},"end":{"line":3,"character":12}}}]""")
                .RootElement.Clone();
        }

        public int CodeLensCalls { get; private set; }
        public int ResolveCalls { get; private set; }
        public JsonElement? LastResolveParams { get; private set; }
        public string? LastCommand { get; private set; }
        public JsonElement? LastCommandParams { get; private set; }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            LastInitializeParams = p.Clone();
            return JsonDocument.Parse($$"""{"capabilities":{{capabilities}}}""").RootElement.Clone();
        }

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
