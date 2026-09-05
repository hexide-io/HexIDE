using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What the client actually claims at initialization, observed from the other end of a real connection.
///
/// <para>
/// Not by constructing the capabilities record and serializing it — that proves the record works, which
/// was never in doubt, and stays green if the client never sends it. Declaring is one half of a
/// negotiation whose other half is a gate, and our own server hides a missing declaration completely: it
/// ignores client capabilities and answers the same to everyone. So the omission would surface only
/// against a server we did not write, as a server that inexplicably never asks for saves.
/// </para>
/// </summary>
public class SaveHandshakeTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];

    /// <summary>Captures the <c>initialize</c> parameters the client sends.</summary>
    private sealed class HandshakeRecorder
    {
        private readonly TaskCompletionSource<JsonElement> _initialize =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JsonElement> InitializeParams => _initialize.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            _initialize.TrySetResult(p.Clone());
            return JsonDocument.Parse(
                """{"capabilities":{"textDocumentSync":{"openClose":true,"change":1}}}""")
                .RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }

    private VBLspClient ClientTalkingTo(HandshakeRecorder server)
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "markdown");
        _disposables.Add(client);
        return client;
    }

    [Fact]
    public async Task TheClientTellsTheServerItCanSendSaveNotifications()
    {
        var server = new HandshakeRecorder();
        var client = ClientTalkingTo(server);

        await client.StartAsync();
        var initialize = await server.InitializeParams.WaitAsync(TimeSpan.FromSeconds(10));

        initialize.GetProperty("capabilities")
            .GetProperty("textDocument")
            .GetProperty("synchronization")
            .GetProperty("didSave").GetBoolean()
            .Should().BeTrue(
                "a server has no reason to offer `save` to a client that never said it could receive one");
    }

    [Fact]
    public async Task TheClientClaimsNoSaveCapabilityItDoesNotImplement()
    {
        // Observed on the wire rather than on the record, for the same reason as above. A server may
        // reasonably block on a willSaveWaitUntil response, and one that never comes is a hang rather than
        // a missing feature.
        var server = new HandshakeRecorder();
        var client = ClientTalkingTo(server);

        await client.StartAsync();
        var initialize = await server.InitializeParams.WaitAsync(TimeSpan.FromSeconds(10));

        var synchronization = initialize.GetProperty("capabilities")
            .GetProperty("textDocument").GetProperty("synchronization");

        synchronization.TryGetProperty("willSave", out _).Should().BeFalse();
        synchronization.TryGetProperty("willSaveWaitUntil", out _).Should().BeFalse();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }
}
