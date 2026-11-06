using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Tests.LspClient;

public class CodeActionTests : IAsyncDisposable
{
    private readonly Peer peer = new();
    private JsonRpc? server;
    private VBLspClient? client;
    private static readonly HexIDE.Lsp.Messages.Range Range = new(new(2, 1), new(2, 5));

    private async Task<ILspClient> Connect(bool start = true)
    {
        var (local, remote) = FullDuplexStream.CreatePair();
        server = new JsonRpc(new HeaderDelimitedMessageHandler(remote, remote, new SystemTextJsonFormatter()), peer);
        server.StartListening();
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(local, local, ci.Arg<IJsonRpcMessageFormatter>())));
        client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
        if (start) await client.StartAsync(TestContext.Current.CancellationToken);
        return client;
    }

    [Fact]
    public async Task DiagnosticsAndOpaqueDataReachTheServerUnchanged()
    {
        var sut = await Connect();
        const string data = "{\"number\":1.00, \"escaped\":\"\\u0041\"}";
        using var token = JsonDocument.Parse(data);
        var diagnostic = new Diagnostic(Range, "problem", Data: token.RootElement.Clone());
        var actions = await sut.RequestCodeActionsAsync("file:///a.bas", Range, [diagnostic], TestContext.Current.CancellationToken);
        actions.Select(a => a.Title).Should().Equal("Fix", "Unavailable", "Lazy");
        peer.Request!.Value.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be("file:///a.bas");
        peer.Request.Value.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32().Should().Be(2);
        var context = peer.Request.Value.GetProperty("context");
        context.GetProperty("triggerKind").GetInt32().Should().Be(1);
        context.GetProperty("diagnostics")[0].GetProperty("data").GetRawText().Should().Be(data);
        actions[1].DisabledReason.Should().Be("Save first");
    }

    [Fact]
    public async Task InitializeDeclaresTheRelayCapabilities()
    {
        await Connect();
        var textDocument = peer.InitializeParams!.Value.GetProperty("capabilities").GetProperty("textDocument");
        textDocument.GetProperty("publishDiagnostics").GetProperty("dataSupport").GetBoolean().Should().BeTrue();
        var caps = textDocument.GetProperty("codeAction");
        caps.GetProperty("dataSupport").GetBoolean().Should().BeTrue();
        caps.GetProperty("disabledSupport").GetBoolean().Should().BeTrue();
        caps.GetProperty("codeActionLiteralSupport").GetProperty("codeActionKind").GetProperty("valueSet")
            .EnumerateArray().Select(v => v.GetString()).Should().Contain("quickfix");
        caps.GetProperty("resolveSupport").GetProperty("properties").EnumerateArray().Select(v => v.GetString()).Should().Equal("edit");
    }

    [Fact]
    public async Task LazyPayloadIsResolvedAndItsCommandReturnsToItsOrigin()
    {
        var sut = await Connect();
        var actions = await sut.RequestCodeActionsAsync("file:///a.bas", Range, [], TestContext.Current.CancellationToken);
        var resolved = await actions[2].Resolve!(TestContext.Current.CancellationToken);
        peer.ResolveParams!.Value.GetProperty("data").GetProperty("id").GetInt32().Should().Be(42);
        resolved.Edit.Should().NotBeNull();
        await resolved.Execute!(resolved.Command!, TestContext.Current.CancellationToken);
        peer.CommandParams!.Value.GetProperty("command").GetString().Should().Be("server.fix");
        peer.CommandParams.Value.GetProperty("arguments")[0].GetInt32().Should().Be(42);
        peer.Order.Should().Equal("request", "resolve", "command");
    }

    [Fact]
    public async Task BareCommandsAreKeptAndExecutedWithoutResolve()
    {
        peer.Actions = """[{"title":"Run","command":"server.fix","arguments":[7]}]""";
        var sut = await Connect();
        var action = (await sut.RequestCodeActionsAsync("file:///a.bas", Range, [], TestContext.Current.CancellationToken)).Single();
        action.Resolve.Should().BeNull();
        await action.Execute!(action.Command!, TestContext.Current.CancellationToken);
        peer.CommandParams!.Value.GetProperty("arguments")[0].GetInt32().Should().Be(7);
        peer.ResolveParams.Should().BeNull();
    }

    [Fact]
    public async Task StartupAndAnUnofferingServerStayQuietUntilACapableServerIsAsked()
    {
        peer.Capabilities = "{}";
        var sut = await Connect();
        peer.Request.Should().BeNull();
        (await sut.RequestCodeActionsAsync("file:///a.bas", Range, [], TestContext.Current.CancellationToken)).Should().BeEmpty();
        peer.Request.Should().BeNull();
        await client!.DisposeAsync();
        server!.Dispose();
        peer.Capabilities = "{\"codeActionProvider\":true}";
        sut = await Connect();
        await sut.RequestCodeActionsAsync("file:///a.bas", Range, [], TestContext.Current.CancellationToken);
        peer.Request.Should().NotBeNull();
    }

    [Fact]
    public async Task EveryOfferingClaimantContributesWithItsServerIdentity()
    {
        var capable = new[] { Substitute.For<ILspClient>(), Substitute.For<ILspClient>() };
        foreach (var c in capable)
        {
            c.IsRunning.Returns(true);
            using var caps = JsonDocument.Parse("""{"codeActionProvider":true}""");
            c.AdvertisedCapabilities.Returns(caps.RootElement.Clone());
            c.RequestCodeActionsAsync(Arg.Any<string>(), Range, Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>())
                .Returns([new OfferedCodeAction("", "Fix", null, null, null)]);
        }
        var silent = Substitute.For<ILspClient>();
        silent.IsRunning.Returns(true);
        await using var registry = new LspClientRegistry([
            new LanguageServerRegistration("first", "First", [".bas"], "vb6", () => capable[0]),
            new LanguageServerRegistration("second", "Second", [".bas"], "vb6", () => capable[1]),
            new LanguageServerRegistration("silent", "Silent", [".bas"], "vb6", () => silent)],
            Substitute.For<ILogger<LspClientRegistry>>());
        await registry.OpenDocumentAsync("file:///a.bas", "code", TestContext.Current.CancellationToken);
        var diagnostic = new Diagnostic(Range, "problem");
        foreach (var owner in new[] { "first", "second" })
            await registry.InjectDiagnosticsAsync("file:///a.bas", [diagnostic], owner);
        var actions = await registry.RequestCodeActionsAsync("file:///a.bas", Range, [diagnostic], TestContext.Current.CancellationToken);
        actions.Select(a => a.Server).Should().BeEquivalentTo("first", "second");
        await silent.DidNotReceive().RequestCodeActionsAsync(Arg.Any<string>(), Arg.Any<HexIDE.Lsp.Messages.Range>(), Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnlistedActionCommandsNeverReachTheServer()
    {
        peer.Actions = """[{"title":"Run","command":"unknown.fix"}]""";
        var sut = await Connect();
        var action = (await sut.RequestCodeActionsAsync("file:///a.bas", Range, [], TestContext.Current.CancellationToken)).Single();
        await action.Execute!(action.Command!, TestContext.Current.CancellationToken);
        peer.CommandParams.Should().BeNull();
    }

    [Fact]
    public async Task EachClaimantReceivesOnlyItsOwnDiagnosticData()
    {
        var clients = new[] { Substitute.For<ILspClient>(), Substitute.For<ILspClient>() };
        using var caps = JsonDocument.Parse("""{"codeActionProvider":true}""");
        foreach (var c in clients)
        {
            c.IsRunning.Returns(true);
            c.AdvertisedCapabilities.Returns(caps.RootElement.Clone());
            c.RequestCodeActionsAsync(Arg.Any<string>(), Range, Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>())
                .Returns([new OfferedCodeAction("", "Fix", null, null, null)]);
        }
        await using var registry = new LspClientRegistry([
            new LanguageServerRegistration("one", "First server", [".bas"], "vb6", () => clients[0]),
            new LanguageServerRegistration("two", "Second server", [".bas"], "vb6", () => clients[1])],
            Substitute.For<ILogger<LspClientRegistry>>());
        const string uri = "file:///a.bas";
        await registry.OpenDocumentAsync(uri, "code", TestContext.Current.CancellationToken);
        using var data = JsonDocument.Parse("""{"token":1.00}""");
        var first = new Diagnostic(Range, "same", Data: data.RootElement.Clone());
        var second = new Diagnostic(Range, "same", Data: JsonSerializer.SerializeToElement(2));
        clients[0].DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(clients[0], new PublishDiagnosticsParams(uri, [first]));
        clients[1].DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(clients[1], new PublishDiagnosticsParams(uri, [second]));
        var actions = await registry.RequestCodeActionsAsync(uri, Range, [first, second], TestContext.Current.CancellationToken);
        for (var i = 0; i < 2; i++)
        {
            var expected = i == 0 ? first : second;
            await clients[i].Received(1).RequestCodeActionsAsync(uri, Range,
                Arg.Is<Diagnostic[]>(ds => ds.Length == 1 && ReferenceEquals(ds[0], expected)), Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task EachClaimantReceivesItsOwnFirstDiagnosticRangeOnTheWire()
    {
        await using var first = new CodeActionTests();
        await using var second = new CodeActionTests();
        var clients = new[] { await first.Connect(false), await second.Connect(false) };
        await using var registry = new LspClientRegistry([
            new LanguageServerRegistration("one", "One", [".bas"], "vb6", () => clients[0]),
            new LanguageServerRegistration("two", "Two", [".bas"], "vb6", () => clients[1])],
            Substitute.For<ILogger<LspClientRegistry>>());
        const string uri = "file:///a.bas";
        await registry.OpenDocumentAsync(uri, "code", TestContext.Current.CancellationToken);
        var published = new TaskCompletionSource<Diagnostic[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.DiagnosticsPublished += (_, p) =>
        {
            if (p.Diagnostics.Length == 2) published.TrySetResult(p.Diagnostics);
        };
        var otherRange = new HexIDE.Lsp.Messages.Range(new(2, 0), new(2, 8));
        await first.server!.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics",
            new PublishDiagnosticsParams(uri, [new Diagnostic(Range, "first")]));
        await second.server!.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics",
            new PublishDiagnosticsParams(uri, [new Diagnostic(otherRange, "second")]));
        var diagnostics = await published.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await registry.RequestCodeActionsAsync(uri, Range, diagnostics, TestContext.Current.CancellationToken);
        first.peer.Request!.Value.GetProperty("range").Deserialize<HexIDE.Lsp.Messages.Range>().Should().Be(Range);
        second.peer.Request!.Value.GetProperty("range").Deserialize<HexIDE.Lsp.Messages.Range>().Should().Be(otherRange);
        first.peer.Request.Value.GetProperty("context").GetProperty("diagnostics")[0].GetProperty("message").GetString().Should().Be("first");
        second.peer.Request.Value.GetProperty("context").GetProperty("diagnostics")[0].GetProperty("message").GetString().Should().Be("second");
    }

    [Fact]
    public async Task ClaimantWithoutADiagnosticAtTheCaretIsNotAsked()
    {
        var sut = await Connect(false);
        await using var registry = new LspClientRegistry([
            new LanguageServerRegistration("one", "One", [".bas"], "vb6", () => sut)],
            Substitute.For<ILogger<LspClientRegistry>>());
        const string uri = "file:///a.bas";
        await registry.OpenDocumentAsync(uri, "code", TestContext.Current.CancellationToken);
        await registry.RequestCodeActionsAsync(uri, Range, [new Diagnostic(Range, "another server")], TestContext.Current.CancellationToken);
        peer.Request.Should().BeNull();
    }

    [Fact]
    public async Task ActionGroupUsesTheServerDisplayName()
    {
        var source = Substitute.For<ILspClient>();
        source.IsRunning.Returns(true);
        using var caps = JsonDocument.Parse("""{"codeActionProvider":true}""");
        source.AdvertisedCapabilities.Returns(caps.RootElement.Clone());
        source.RequestCodeActionsAsync(Arg.Any<string>(), Range, Arg.Any<Diagnostic[]>(), Arg.Any<CancellationToken>())
            .Returns([new OfferedCodeAction("", "Fix", null, null, null)]);
        await using var registry = new LspClientRegistry([
            new LanguageServerRegistration("rdcore-vba", "RDCore VBA", [".bas"], "vb6", () => source)],
            Substitute.For<ILogger<LspClientRegistry>>());
        await registry.OpenDocumentAsync("file:///a.bas", "code", TestContext.Current.CancellationToken);
        var diagnostic = new Diagnostic(Range, "problem");
        await registry.InjectDiagnosticsAsync("file:///a.bas", [diagnostic], "rdcore-vba");
        var actions = await registry.RequestCodeActionsAsync("file:///a.bas", Range, [diagnostic], TestContext.Current.CancellationToken);
        var action = actions.Should().ContainSingle().Which;
        action.Server.Should().Be("rdcore-vba");
        action.ServerDisplayName.Should().Be("RDCore VBA");
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null) await client.DisposeAsync();
        server?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class Peer
    {
        public string Capabilities = """{"codeActionProvider":{"resolveProvider":true},"executeCommandProvider":{"commands":["server.fix"]}}""";
        public string Actions = """[{"title":"Fix","edit":{"changes":{}}},{"title":"Unavailable","disabled":{"reason":"Save first"}},{"title":"Lazy","data":{"id":42}}]""";
        public JsonElement? InitializeParams, Request, ResolveParams, CommandParams;
        public List<string> Order = [];
        private static JsonElement Json(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p) { InitializeParams = p.Clone(); return Json("{\"capabilities\":" + Capabilities + "}"); }
        [JsonRpcMethod("initialized")] public void Initialized(JsonElement _) { }
        [JsonRpcMethod("shutdown")] public object? Shutdown() => null;
        [JsonRpcMethod("exit")] public void Exit() { }
        [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
        public JsonElement ActionsFor(JsonElement p) { Request = p.Clone(); Order.Add("request"); return Json(Actions); }
        [JsonRpcMethod("codeAction/resolve", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Resolve(JsonElement p)
        {
            ResolveParams = p.Clone(); Order.Add("resolve");
            return Json("""{"title":"Lazy","edit":{"changes":{}},"command":{"title":"Run","command":"server.fix","arguments":[42]}}""");
        }
        [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
        public object? Command(JsonElement p) { CommandParams = p.Clone(); Order.Add("command"); return null; }
    }
}
