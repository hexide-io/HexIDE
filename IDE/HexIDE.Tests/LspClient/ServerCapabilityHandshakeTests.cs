using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Drives the initialize handshake against a stub server that answers for real, because the defect these
/// pin (#238) lives in deserializing the reply — a substituted transport that never produces one cannot
/// reach it.
/// </summary>
public class ServerCapabilityHandshakeTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    /// <summary>
    /// Builds a client wired to a stub server that returns <paramref name="capabilitiesJson"/> verbatim as
    /// its capabilities object.
    /// </summary>
    private VBLspClient ClientTalkingToServerAdvertising(string capabilitiesJson) =>
        ClientTalkingToServer(new StubServer(capabilitiesJson));

    private VBLspClient ClientTalkingToServer(object serverTarget)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            serverTarget);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        // IsRunning is `transport.IsAlive && initialized`, and a substitute reports false by default.
        // These tests are about the second half, so the first has to be true or they assert nothing.
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        _disposables.Add(client);
        return client;
    }

    [Fact]
    public async Task TheMeasuredCase_ACapabilityAdvertisedAsAnOptionsObject_StillInitializes()
    {
        // #238. `hoverProvider` is `boolean | HoverOptions` in the protocol, and a conformant server may
        // answer either. Modelled as `bool?`, the options form threw during deserialization, the catch
        // swallowed it, and every language feature — diagnostics included — went dark with no error.
        var sut = ClientTalkingToServerAdvertising(
            """{"hoverProvider":{"workDoneProgress":false},"renameProvider":{"prepareProvider":true}}""");

        await sut.StartAsync();

        sut.IsRunning.Should().BeTrue(
            "a capability in its other legal shape must not disable the entire language client");
    }

    [Fact]
    public async Task ACapabilityAdvertisedAsAPlainFlagStillInitializes()
    {
        var sut = ClientTalkingToServerAdvertising("""{"hoverProvider":true,"definitionProvider":true}""");

        await sut.StartAsync();

        sut.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task AReplyThatCannotBeModelledAtAllStillLeavesTheClientRunning()
    {
        // The point of separating the two failures. The server completed the handshake; we simply cannot
        // describe what it told us. Losing knowledge is acceptable, losing the connection is not.
        var sut = ClientTalkingToServerAdvertising("""["not", "an", "object"]""");

        await sut.StartAsync();

        sut.IsRunning.Should().BeTrue("the handshake succeeded even though its reply was uninterpretable");
    }

    [Fact]
    public async Task AServerAdvertisingNothingStillInitializes()
    {
        // Both halves of what this comment used to say are now false, which is why it has been rewritten
        // rather than left: our own server advertises honestly, and gating is implemented and in use — for
        // open/close, for every request-based feature, and for saves.
        //
        // The test is worth MORE than it was, not less. An empty payload is still legal, a foreign server
        // may still send one, and every gate now reads that payload — so "advertises nothing" has gone
        // from a state we tolerated to a state we act on, and it must still initialize rather than being
        // treated as a failed handshake.
        var sut = ClientTalkingToServerAdvertising("{}");

        await sut.StartAsync();

        sut.IsRunning.Should().BeTrue();
    }

    [Theory]
    // Enabled: both legal shapes.
    [InlineData("""{"hoverProvider":true}""", true)]
    [InlineData("""{"hoverProvider":{"workDoneProgress":false}}""", true)]
    // Not enabled: explicit false, and absent.
    [InlineData("""{"hoverProvider":false}""", false)]
    [InlineData("{}", false)]
    public void IsEnabled_AcceptsAnOptionsObjectAsSupport(string capsJson, bool expected)
    {
        // An options object IS the enabled form — a server returning options is describing *how* it
        // supports a feature, which necessarily means it does.
        var caps = JsonSerializer.Deserialize(capsJson, LspJsonContext.Default.ServerCapabilities);

        ServerCapabilities.IsEnabled(caps!.HoverProvider).Should().Be(expected);
    }

    [Theory]
    [InlineData("""{"textDocumentSync":1}""", true)]                              // Full, bare number
    [InlineData("""{"textDocumentSync":2}""", true)]                              // Incremental
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1}}""", true)]  // object form
    [InlineData("""{"textDocumentSync":0}""", false)]                             // kind 0 = send nothing
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":0}}""", false)]
    [InlineData("{}", false)]
    public void AcceptsDocumentSync_ReadsBothShapesAndHonoursKindZero(string capsJson, bool expected)
    {
        // textDocumentSync is the one field whose two shapes say different things rather than the same
        // thing twice — so it cannot go through IsEnabled, where an object always means yes.
        var caps = JsonSerializer.Deserialize(capsJson, LspJsonContext.Default.ServerCapabilities);

        caps!.AcceptsDocumentSync().Should().Be(expected);
    }

    [Fact]
    public async Task AFeatureTheServerDidNotAdvertiseIsNotEvenRequested()
    {
        // The stub WOULD answer hover. Advertising nothing must stop us asking — so a null here proves the
        // request was gated, not that the server had nothing to say.
        var sut = ClientTalkingToServerAdvertising("{}");
        await sut.StartAsync();

        var hover = await sut.RequestHoverAsync("vb6://module/M", new Position(0, 0));

        hover.Should().BeNull("the server advertised no hoverProvider, so it must not be asked");
    }

    [Fact]
    public async Task AFeatureTheServerDidAdvertiseIsRequestedNormally()
    {
        // The control for the test above. Without it, "returns null" would pass even if gating had broken
        // into refusing everything.
        var sut = ClientTalkingToServerAdvertising("""{"hoverProvider":true}""");
        await sut.StartAsync();

        var hover = await sut.RequestHoverAsync("vb6://module/M", new Position(0, 0));

        hover.Should().NotBeNull();
        hover!.Contents.Value.Should().Be("stub hover");
    }

    [Fact]
    public async Task AnAdvertisedCapabilityInItsOptionsFormStillPermitsTheRequest()
    {
        // Gating reads a capability in either legal shape, or #238 would come back as "supported feature
        // silently refused" instead of "handshake fails".
        var sut = ClientTalkingToServerAdvertising("""{"hoverProvider":{"workDoneProgress":false}}""");
        await sut.StartAsync();

        (await sut.RequestHoverAsync("vb6://module/M", new Position(0, 0))).Should().NotBeNull();
    }

    [Theory]
    // A bare sync kind, and the object form, both mean "send me changes".
    [InlineData("""{"textDocumentSync":1}""", true)]
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":1}}""", true)]
    // Kind 0 means "send me nothing" — the one place an object must NOT be read as yes.
    [InlineData("""{"textDocumentSync":{"openClose":true,"change":0}}""", false)]
    [InlineData("{}", false)]
    public void DocumentSyncIsGatedOnTheSyncKindRatherThanOnPresence(string capsJson, bool expected)
    {
        var caps = JsonDocument.Parse(capsJson).RootElement;

        ServerCapabilities.AcceptsChanges(caps).Should().Be(expected);
    }

    [Fact]
    public async Task AServerThatFailsTheHandshakeLeavesTheClientDisabled()
    {
        // THE control, and it has to be this shape. Every assertion above is "still running", which keeps
        // passing if IsRunning ever became unconditionally true. Asserting against a transport that reports
        // no server would not catch that, because IsRunning is `IsAlive && initialized` and IsAlive alone
        // would carry the assertion. So this connects a live transport to a server that refuses initialize:
        // the only thing left that can make IsRunning false is the flag under test.
        var sut = ClientTalkingToServer(new RefusingServer());

        await sut.StartAsync();

        sut.IsRunning.Should().BeFalse(
            "the transport is alive, so only a genuinely failed handshake can produce this");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
    }

    /// <summary>A minimal server: answers initialize with the given capabilities, ignores everything else.</summary>
    private sealed class StubServer(string capabilitiesJson)
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse($$"""{"capabilities":{{capabilitiesJson}}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Hover(JsonElement _) =>
            JsonDocument.Parse("""{"contents":{"kind":"plaintext","value":"stub hover"}}""").RootElement.Clone();
    }

    /// <summary>A server that refuses to initialize — the negative case for the control above.</summary>
    private sealed class RefusingServer
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => throw new InvalidOperationException("nope");
    }
}
