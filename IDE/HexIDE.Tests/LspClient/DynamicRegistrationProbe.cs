using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What actually happens when a server sends <c>client/registerCapability</c>.
///
/// <para>
/// Written before any fix, because the issue that prompted it (hexide-io/HexIDE#288) asserted a
/// consequence nobody here had observed. The client registers no handler for that request, and what a
/// server gets back was assumed to be a JSON-RPC error on StreamJsonRpc's account rather than measured.
/// "Very probably an error" is not a basis for changing a protocol seam.
/// </para>
/// </summary>
public class DynamicRegistrationProbe : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _clients = [];
    private readonly List<IDisposable> _disposables = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _clients)
        {
            try { await c.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private sealed class RegisteringServer
    {
        public JsonRpc? Rpc;
        public JsonElement ClientCapabilities;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p)
        {
            ClientCapabilities = p.TryGetProperty("capabilities", out var caps)
                ? caps.Clone()
                : default;
            // Deliberately advertises NOTHING but sync — the shape a server takes when it intends to
            // register the rest dynamically.
            return JsonDocument.Parse("""{"capabilities":{"textDocumentSync":1}}""").RootElement.Clone();
        }

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        /// <summary>Asks the client to register hover, the way a server built on the reference library would.</summary>
        public Task<JsonElement> RegisterHoverAsync() =>
            Rpc!.InvokeWithParameterObjectAsync<JsonElement>("client/registerCapability", new
            {
                registrations = new[]
                {
                    new { id = "hover-1", method = "textDocument/hover", registerOptions = new { } },
                },
            });
    }

    private sealed class RecordingLogger : ILogger<VBLspClient>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get { lock (_entries) return _entries.ToArray(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private async Task<(VBLspClient Client, RegisteringServer Server)> ConnectedAsync(
        ILogger<VBLspClient>? logger = null)
    {
        var server = new RegisteringServer();
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()), server);
        server.Rpc = serverRpc;
        serverRpc.StartListening();
        _disposables.Add(serverRpc);

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(
            transport, logger ?? Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        _clients.Add(client);
        await client.StartAsync();
        return (client, server);
    }

    [Fact]
    public async Task TheClientDoesNotClaimToSupportDynamicRegistration()
    {
        // The half of this that is settled by the specification rather than by our behaviour.
        // `dynamicRegistration` is a CLIENT capability — "whether hover supports dynamic registration" —
        // so a conformant server may only register dynamically for something the client asked for. We ask
        // for nothing, so a conformant server must declare everything statically at initialize.
        var (_, server) = await ConnectedAsync();

        var json = server.ClientCapabilities.GetRawText();

        // Asserted positively first. A capabilities object that never arrived would also "not contain"
        // the thing being checked for, and the test would pass while measuring nothing.
        json.Should().Contain("publishDiagnostics",
            "the capabilities must actually have reached the server for the next assertion to mean anything");

        json.Should().NotContain("dynamicRegistration",
            "if this ever appears, a conformant server may start registering dynamically and the "
          + "registrations must actually be honoured");
    }

    [Fact]
    public async Task AServerRegisteringAnywayGetsAnErrorRatherThanSilence()
    {
        // The half that is ours, and the reason this file exists: measured, not assumed. A server that
        // registers regardless — non-conformant, but they exist — must get an answer. Silence would leave
        // it awaiting a reply forever, which is the failure shape this client has already been bitten by
        // (hexide-io/HexIDE#231).
        var (_, server) = await ConnectedAsync();

        var register = server.RegisterHoverAsync();
        // xUnit1051 suppressed deliberately: this Task.Delay is the TIMEOUT ARM of the race, not
        // work the test is waiting on. Giving it the test's cancellation token would make the
        // timeout itself cancellable — the guard would vanish exactly when a cancelled run most
        // needs it to fire, and WhenAny would settle on a faulted task rather than a timeout.
#pragma warning disable xUnit1051
        var finished = await Task.WhenAny(register, Task.Delay(TimeSpan.FromSeconds(10)));
#pragma warning restore xUnit1051

        finished.Should().BeSameAs(register, "an unanswered request would hang the server, not degrade it");

        var thrown = await Record.ExceptionAsync(() => register);
        thrown.Should().BeOfType<RemoteMethodNotFoundException>(
            "the honest answer to a request we do not implement is a JSON-RPC error naming that, which "
          + "lets a conformant server fall back rather than wait");
    }

    [Fact]
    public async Task RefusingIsWrittenDown_SoMissingFeaturesHaveATrace()
    {
        // The only thing this change actually altered. The wire answer was already right; what was absent
        // was any local record, so a server that asked, was refused, and served less left the user with
        // fewer features and nothing to explain them. That is the same silence #289 closed on a different
        // channel, and it is the whole of #288 once the spec question is settled.
        var logger = new RecordingLogger();
        var (_, server) = await ConnectedAsync(logger);

        await Record.ExceptionAsync(() => server.RegisterHoverAsync());

        logger.Entries.Should().ContainSingle(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("register capabilities dynamically"),
            "a refusal nobody can see is the bug this issue was actually about");
    }

    [Fact]
    public async Task TheLogNamesWhatWasRefused_NotJustThatSomethingWas()
    {
        // "A server asked for something" is not actionable. Which method it wanted is.
        var logger = new RecordingLogger();
        var (_, server) = await ConnectedAsync(logger);

        await Record.ExceptionAsync(() => server.RegisterHoverAsync());

        logger.Entries.Should().Contain(e => e.Message.Contains("textDocument/hover"),
            "the method it wanted is what tells a reader which feature went missing");
    }
}
