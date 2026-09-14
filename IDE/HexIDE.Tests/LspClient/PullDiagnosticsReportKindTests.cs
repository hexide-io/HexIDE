using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The half of the pull model that no real server here can demonstrate.
///
/// <para>
/// A server answering a diagnostics request may reply "what I told you last time still stands" instead of
/// repeating itself. That reply carries no diagnostics at all, so a client treating it as an empty set
/// <b>erases exactly the marks it was sent to preserve</b> — and only on the second request, so they appear
/// correctly and then vanish. It is the one genuinely destructive way to get this feature wrong.
/// </para>
///
/// <para>
/// <b>Scripted rather than foreign, because the foreign server cannot reach it.</b> Measured against ruff
/// 0.16.7: it returns no <c>resultId</c> at all, so it never has one to refer back to and never sends an
/// <c>unchanged</c> report. Left to the foreign suite this branch would sit unexercised behind a passing
/// test, which is the shape of unverified coverage this project keeps finding in itself. Here the server's
/// answer can be dictated, which is the only way to ask the question.
/// </para>
/// </summary>
public class PullDiagnosticsReportKindTests : IAsyncDisposable
{
    private const string Uri = "file:///c:/proj/sample.py";

    private readonly List<IAsyncDisposable> _clients = [];
    private readonly List<IDisposable> _disposables = [];

    /// <summary>
    /// A server that answers only when asked, and the second time says nothing has changed.
    /// </summary>
    private sealed class PullingServer
    {
        private int _asked;

        public int TimesAsked => Volatile.Read(ref _asked);

        /// <summary>What the client sent back as <c>previousResultId</c> on the most recent request.</summary>
        public string? LastPreviousResultId { get; private set; }

        /// <summary>What the client sent as <c>identifier</c> on the most recent request.</summary>
        public string? LastIdentifier { get; private set; }

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse("""
            {
              "capabilities": {
                "textDocumentSync": { "openClose": true, "change": 1 },
                "diagnosticProvider": {
                  "identifier": "scripted",
                  "interFileDependencies": false,
                  "workspaceDiagnostics": false
                }
              }
            }
            """).RootElement.Clone();

        [JsonRpcMethod("textDocument/diagnostic", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Diagnostic(JsonElement p)
        {
            LastPreviousResultId =
                p.TryGetProperty("previousResultId", out var previous) && previous.ValueKind == JsonValueKind.String
                    ? previous.GetString()
                    : null;
            LastIdentifier =
                p.TryGetProperty("identifier", out var identifier) && identifier.ValueKind == JsonValueKind.String
                    ? identifier.GetString()
                    : null;

            // First answer: a full report, with a name for itself. Afterwards: that name, unchanged.
            return Interlocked.Increment(ref _asked) == 1
                ? JsonDocument.Parse("""
                    {
                      "kind": "full",
                      "resultId": "the-first-answer",
                      "items": [
                        {
                          "range": { "start": { "line": 0, "character": 0 },
                                     "end":   { "line": 0, "character": 9 } },
                          "message": "`os` imported but unused",
                          "severity": 2,
                          "source": "scripted"
                        }
                      ]
                    }
                    """).RootElement.Clone()
                : JsonDocument.Parse("""
                    { "kind": "unchanged", "resultId": "the-first-answer" }
                    """).RootElement.Clone();
        }
    }

    /// <summary>
    /// A server that publishes, and says nothing about answering when asked.
    /// </summary>
    /// <remarks>
    /// Scripted rather than one of the foreign servers, because of a measured surprise: <c>rumdl</c>,
    /// <b>which publishes</b>, also advertises <c>diagnosticProvider</c> — so asking it is correct, and it
    /// cannot serve as the negative case. Supporting both models is legal and apparently not rare. Since the
    /// gate being tested is client-side logic, a server whose capabilities are dictated proves it without
    /// depending on a third party's continuing choice not to implement something.
    /// </remarks>
    private sealed class PublishingServer
    {
        private int _asked;
        public int TimesAsked => Volatile.Read(ref _asked);

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse("""
            { "capabilities": { "textDocumentSync": { "openClose": true, "change": 1 } } }
            """).RootElement.Clone();

        [JsonRpcMethod("textDocument/diagnostic", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Diagnostic(JsonElement _)
        {
            Interlocked.Increment(ref _asked);
            return JsonDocument.Parse("""{ "kind": "full", "items": [] }""").RootElement.Clone();
        }
    }

    [Fact]
    public void AFrameWithNullParametersIsReadRatherThanKillingTheConnection()
    {
        // The defect this found, which is HexIDE's rather than the server's: a frame that cannot be decoded
        // surfaces as a STREAM error, and a stream error ends the connection — so one malformed notification
        // took every language feature with it, permanently, on a transport that cannot re-dial.
        //
        // rumdl sends exactly this once the client declares it can ask for diagnostics:
        //   {"jsonrpc":"2.0","method":"workspace/diagnostic/refresh","params":null,"id":1}
        // `params: null` is invalid JSON-RPC, and `workspace/diagnostic/refresh` takes no parameters at all
        // — so reading it as though the member were absent recovers precisely what the server meant.
        //
        // Asserted against the repair directly rather than through a live connection: the failure being
        // guarded is inside the formatter's reader, and driving it end to end would make a passing test
        // depend on a third party continuing to send a malformed frame.
        var malformed = System.Text.Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","method":"workspace/diagnostic/refresh","params":null,"id":1}""");

        var repaired = CapturingFormatter.RepairNullParamsForTests(
            new System.Buffers.ReadOnlySequence<byte>(malformed));

        repaired.Should().NotBeNull("a null `params` is the one case that has a single safe reading");
        using var document = JsonDocument.Parse(repaired!);
        document.RootElement.TryGetProperty("params", out _).Should().BeFalse(
            "the member is dropped, which is what 'takes no parameters' means on the wire");
        document.RootElement.GetProperty("method").GetString().Should().Be("workspace/diagnostic/refresh",
            "and nothing else about the frame may change");
        document.RootElement.GetProperty("id").GetInt32().Should().Be(1,
            "the id above all — losing it would leave the server waiting for a reply that never comes");
    }

    [Fact]
    public void AFrameThatIsMerelyDifferentIsNotRepaired()
    {
        // The repair must stay a repair. Rewriting frames whose meaning is not in doubt is how a client
        // starts quietly accepting messages that say something other than what it decides they say.
        var wellFormed = System.Text.Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","method":"window/logMessage","params":{"type":3,"message":"hello"}}""");

        CapturingFormatter.RepairNullParamsForTests(
                new System.Buffers.ReadOnlySequence<byte>(wellFormed))
            .Should().BeNull("there is nothing wrong with it, so it must be left exactly as it arrived");
    }

    [Fact]
    public async Task AServerThatDidNotOfferToAnswerIsNotAsked()
    {
        // Gating must EXCLUDE, or this would pass with a client that asks everybody — and asking a server
        // that never offered is how a client earns a -32601 from a server that is behaving perfectly.
        var server = new PublishingServer();
        var sut = ClientTalkingTo(server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await sut.ChangeDocumentAsync(Uri, 2, "import os\n\n", TestContext.Current.CancellationToken);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        server.TimesAsked.Should().Be(0,
            "it did not advertise that it answers when asked, so it must not be asked");
    }

    [Fact]
    public async Task AServerThatDidNotOfferToAnswerIsNotComplainedAbout()
    {
        // The other half, and the reason this capability has a gate of its own. Every other gate warns when
        // a capability is absent, because absence means a feature is unavailable. Here absence means the
        // server publishes instead — the ordinary case — so a complaint would blame a conformant server for
        // conforming, in the log and in the protocol capture a server author reads.
        var server = new PublishingServer();
        var sut = ClientTalkingTo(server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Task.Delay(500, TestContext.Current.CancellationToken);

        sut.DeclinedCapabilities.Should().NotContain("diagnosticProvider",
            "not offering to answer is a complete answer, not a shortfall to be reported");
    }

    private VBLspClient ClientTalkingTo(object server)
    {
        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.CanReconnect.Returns(false);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var (clientSide, serverSide) = FullDuplexStream.CreatePair();
                var serverRpc = new JsonRpc(
                    new HeaderDelimitedMessageHandler(serverSide, serverSide, new SystemTextJsonFormatter()),
                    server);
                serverRpc.StartListening();
                lock (_disposables) _disposables.Add(serverRpc);

                return Task.FromResult<IJsonRpcMessageHandler?>(
                    new HeaderDelimitedMessageHandler(
                        clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>()));
            });

        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        _clients.Add(client);
        return client;
    }

    /// <summary>Waits for a condition the server reaches asynchronously, rather than sleeping a guess.</summary>
    private static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        condition().Should().BeTrue("{0}", what);
    }

    [Fact]
    public async Task AnAnswerSayingNothingChangedLeavesTheMarksAlone()
    {
        // The destructive case. An `unchanged` report carries no items, so publishing it as an empty set
        // would clear the document — and it would do so on the SECOND request, meaning the diagnostics
        // appear correctly and then disappear on the next keystroke.
        var server = new PullingServer();
        var sut = ClientTalkingTo(server);
        var published = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => { lock (published) published.Add(p); };

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked >= 1, "the client should have asked on open");
        await Until(() => { lock (published) return published.Count == 1; }, "the first answer should arrive");

        await sut.ChangeDocumentAsync(Uri, 2, "import os\n\n", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked >= 2, "the client should have asked again after the change");

        // Given a moment in which a wrong implementation would have published the empty set.
        await Task.Delay(500, TestContext.Current.CancellationToken);

        lock (published)
        {
            published.Should().ContainSingle(
                "an `unchanged` report says the previous answer stands, so there is nothing new to raise");
            published[0].Diagnostics.Should().ContainSingle()
                .Which.Message.Should().Contain("imported but unused");
        }
    }

    [Fact]
    public async Task TheServersOwnNameForItsLastAnswerGoesBackToIt()
    {
        // Without this the server can never answer `unchanged` at all: it has nothing to compare against,
        // so it must send the full set every time. The feature would appear to work and would have thrown
        // away the only thing it is for.
        var server = new PullingServer();
        var sut = ClientTalkingTo(server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked >= 1, "the client should have asked on open");
        server.LastPreviousResultId.Should().BeNull("there was no previous answer to refer to");

        await sut.ChangeDocumentAsync(Uri, 2, "import os\n\n", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked >= 2, "the client should have asked again after the change");

        server.LastPreviousResultId.Should().Be("the-first-answer",
            "the server named its answer, and the next request must refer to it by that name");
    }

    [Fact]
    public async Task TheServerIsAskedByTheNameItGaveItself()
    {
        // `diagnosticProvider.identifier` is the server's own word for its output, and echoing it is the
        // only correct value — a server producing more than one kind of report has no other way to tell
        // which is being asked for.
        var server = new PullingServer();
        var sut = ClientTalkingTo(server);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked >= 1, "the client should have asked on open");

        server.LastIdentifier.Should().Be("scripted");
    }

    [Fact]
    public async Task ClosingTheDocumentClearsWhatTheServerSaidAboutIt()
    {
        // Nothing else would. A publishing server clears a document by publishing an empty set for it; a
        // server that only answers when asked has no way to say anything about a document we have stopped
        // asking about, so without this the marks outlive the editor that showed them.
        var server = new PullingServer();
        var sut = ClientTalkingTo(server);
        var published = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => { lock (published) published.Add(p); };

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Until(() => { lock (published) return published.Count == 1; }, "the first answer should arrive");

        await sut.CloseDocumentAsync(Uri, TestContext.Current.CancellationToken);

        lock (published)
        {
            published.Should().HaveCount(2);
            published[^1].Uri.Should().Be(Uri);
            published[^1].Diagnostics.Should().BeEmpty("a closed document has no diagnostics to show");
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            try { await client.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        lock (_disposables)
        {
            foreach (var d in _disposables)
            {
                try { d.Dispose(); } catch { /* teardown is best effort */ }
            }
        }
        GC.SuppressFinalize(this);
    }
}
