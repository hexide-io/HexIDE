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

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>());
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
        // Our own server does exactly this today. Gating is a later change and is blocked on fixing it;
        // until then an empty payload must remain harmless.
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
    }

    /// <summary>A server that refuses to initialize — the negative case for the control above.</summary>
    private sealed class RefusingServer
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => throw new InvalidOperationException("nope");
    }
}
