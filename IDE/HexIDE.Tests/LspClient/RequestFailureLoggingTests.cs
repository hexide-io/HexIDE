using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What the log says when a request to a language server fails.
///
/// <para>
/// <b>An exception and an empty answer were logged at the same level, which made them the same event.</b>
/// A server replying "no definition here" is normal and constant; a request that <em>threw</em> means the
/// feature did not work. Both landed in one catch at debug, under a log written at Information by default —
/// so a feature could be bound, implemented, advertised and completely inert with no trace anywhere. That
/// is hexide-io/HexIDE#325, found by driving the IDE and watching nothing happen.
/// </para>
///
/// <para>
/// The counterweight is that these requests are issued per keystroke and routinely superseded, so a naive
/// warning would emit a line per keypress and bury what it exists to surface. Both halves are pinned here,
/// because either alone is the wrong answer.
/// </para>
/// </summary>
public class RequestFailureLoggingTests : IAsyncDisposable
{
    private readonly List<IAsyncDisposable> _disposables = [];
    private readonly RecordingLogger _log = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _disposables)
        {
            try { await d.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private async Task<VBLspClient> ConnectedToThrowingServerAsync()
    {
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();

        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            new ThrowingServer());
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, _log, DocumentLanguage.Vb6);
        _disposables.Add(client);
        await client.StartAsync(TestContext.Current.CancellationToken);
        return client;
    }

    [Fact]
    public async Task ARequestThatThrowsIsReportedAtWarning()
    {
        // The whole point. Before this, the only record of a feature failing outright was a debug line the
        // IDE does not write at its default level.
        var sut = await ConnectedToThrowingServerAsync();

        await sut.RequestDocumentSymbolsAsync("vb6://module/M", TestContext.Current.CancellationToken);

        _log.Warnings.Should().ContainSingle()
            .Which.Should().Contain("textDocument/documentSymbol",
                "the line has to name the method, or it says only that something somewhere failed");
    }

    [Fact]
    public async Task TheSecondFailureOfTheSameMethodDropsToDebug()
    {
        // These are asked on every keystroke. A server broken for one method is broken for every call to
        // it, and the second thousand warnings say nothing the first did not.
        var sut = await ConnectedToThrowingServerAsync();

        await sut.RequestDocumentSymbolsAsync("vb6://module/M", TestContext.Current.CancellationToken);
        await sut.RequestDocumentSymbolsAsync("vb6://module/M", TestContext.Current.CancellationToken);
        await sut.RequestDocumentSymbolsAsync("vb6://module/M", TestContext.Current.CancellationToken);

        _log.Warnings.Should().ContainSingle("once per method per connection");
        _log.Debugs.Should().Contain(m => m.Contains("failed again"),
            "the repeats stay available to anyone who turns the level up");
    }

    [Fact]
    public async Task EachMethodGetsItsOwnWarning()
    {
        // Warning once per METHOD, not once per connection: a server that fails hover and folding for
        // different reasons has two problems, and collapsing them hides the second.
        var sut = await ConnectedToThrowingServerAsync();

        await sut.RequestDocumentSymbolsAsync("vb6://module/M", TestContext.Current.CancellationToken);
        await sut.RequestFoldingRangesAsync("vb6://module/M", TestContext.Current.CancellationToken);

        _log.Warnings.Should().HaveCount(2);
        _log.Warnings.Should().Contain(w => w.Contains("textDocument/documentSymbol"));
        _log.Warnings.Should().Contain(w => w.Contains("textDocument/foldingRange"));
    }

    [Fact]
    public async Task ASupersededRequestIsNotAWarning()
    {
        // THE reason this can be a warning at all. Completion, signature help, highlighting and folding are
        // each driven by a CancellationTokenSource replaced on the next keystroke, so a cancelled request is
        // the design working — and warning on it would emit a line per keypress.
        var sut = await ConnectedToThrowingServerAsync();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await sut.RequestDocumentSymbolsAsync("vb6://module/M", cancelled.Token);

        _log.Warnings.Should().BeEmpty("a superseded request is not a failure");
        _log.Debugs.Should().Contain(m => m.Contains("superseded"));
    }

    [Fact]
    public async Task ADeclinedCapabilityIsStillNotAFailure()
    {
        // The neighbouring case, kept distinct. A server that never advertised something is refusing
        // honestly; that already had its own once-per-capability warning and must not gain a second,
        // different one saying the request failed — because no request was made.
        var (clientSide, serverSide) = FullDuplexStream.CreatePair();
        var serverRpc = new JsonRpc(
            new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
            new SilentServer());
        serverRpc.StartListening();

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IJsonRpcMessageHandler?>(
                new HeaderDelimitedMessageHandler(clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>())));

        var client = new VBLspClient(transport, _log, DocumentLanguage.Vb6);
        _disposables.Add(client);
        await client.StartAsync(TestContext.Current.CancellationToken);

        await client.RequestDocumentSymbolsAsync("vb6://module/M", TestContext.Current.CancellationToken);

        _log.Warnings.Should().ContainSingle()
            .Which.Should().Contain("did not advertise",
                "the honest refusal keeps its own wording; it is not a request failure");
    }

    [Fact]
    public async Task AnErrorReplyDoesNotTakeTheWholeConnectionDown()
    {
        // FOUND BY THE TESTS ABOVE, and much more serious than what they were written for.
        //
        // StreamJsonRpc deserializes the `data` member of any JSON-RPC ERROR response into
        // StreamJsonRpc.Protocol.CommonErrorData, using the formatter's options — which are LspJsonContext's.
        // Unregistered, the generated resolver returns null, the error reply cannot be read, and the failure
        // is NOT scoped to the request that caused it: the connection dies with a ParseError.
        //
        // So a server answering -32601 to one method it does not support took the entire language client
        // down. Every server this suite had ever driven answers `null` rather than an error, which is
        // exactly why it went unseen.
        var sut = await ConnectedToThrowingServerAsync();

        await sut.RequestDocumentSymbolsAsync("vb6://module/M", TestContext.Current.CancellationToken);

        sut.IsRunning.Should().BeTrue("one failed request must not disconnect the client");

        // And the connection is still usable, not merely still marked up.
        var hover = await sut.RequestHoverAsync(
            "vb6://module/M", new Position(0, 0), TestContext.Current.CancellationToken);
        hover!.Contents.Value.Should().Be("still here");
    }

    /// <summary>
    /// A server that initializes normally, throws on two language requests, and answers a third.
    /// </summary>
    /// <remarks>
    /// The working method is not decoration: it is what distinguishes "that request failed" from "the
    /// connection is gone", and those were indistinguishable until the error-reply defect above was fixed.
    /// </remarks>
    private sealed class ThrowingServer
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""
                {"capabilities":{"documentSymbolProvider":true,"foldingRangeProvider":true,
                                 "hoverProvider":true}}
                """).RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }

        [JsonRpcMethod("textDocument/documentSymbol", UseSingleObjectParameterDeserialization = true)]
        public JsonElement DocumentSymbol(JsonElement _) => throw new InvalidOperationException("no");

        [JsonRpcMethod("textDocument/foldingRange", UseSingleObjectParameterDeserialization = true)]
        public JsonElement FoldingRange(JsonElement _) => throw new InvalidOperationException("no");

        [JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Hover(JsonElement _) =>
            JsonDocument.Parse("""{"contents":{"kind":"plaintext","value":"still here"}}""")
                .RootElement.Clone();
    }

    /// <summary>A server that advertises nothing, so the client declines to ask rather than failing.</summary>
    private sealed class SilentServer
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) =>
            JsonDocument.Parse("""{"capabilities":{}}""").RootElement.Clone();

        [JsonRpcMethod("initialized")]
        public void Initialized(JsonElement _) { }
    }

    /// <summary>
    /// Records what was logged at which level.
    /// </summary>
    /// <remarks>
    /// A real logger rather than a substitute: <c>ILogger.Log</c> is generic over an internal state type, so
    /// asserting on a mock's call means matching a type the framework does not expose. Twenty lines here
    /// buys an assertion that reads as the thing being claimed.
    /// </remarks>
    private sealed class RecordingLogger : ILogger<VBLspClient>
    {
        public List<string> Warnings { get; } = [];
        public List<string> Debugs { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            switch (logLevel)
            {
                case LogLevel.Warning: lock (Warnings) Warnings.Add(message); break;
                case LogLevel.Debug or LogLevel.Trace: lock (Debugs) Debugs.Add(message); break;
            }
        }
    }
}
