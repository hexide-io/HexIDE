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
    private readonly CancellationTokenSource _stopServers = new();

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
    public void AFrameWithNullParametersIsRewrittenIntoOneThatCanBeRead()
    {
        // NAMED for what it actually checks, after the first name claimed more. It used to say the
        // connection survived, which this test cannot see: it never builds one. That claim belongs to
        // AMalformedFrameDoesNotEndAConnectionNobodyIsRecording, and while the name stood unexamined the
        // repair was in fact installed only on connections that were being recorded.
        //
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

    /// <summary>
    /// A server that hand-writes its frames, rather than being driven by a JSON-RPC library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StreamJsonRpc</c> on the far end cannot produce what these tests need. It will not emit
    /// <c>"params": null</c>, because that is not valid JSON-RPC; and it will not hold a request unanswered
    /// while it sends something else. Both are things real servers do, and both are reachable only by
    /// writing the bytes.
    /// </para>
    /// <para>
    /// The subclass decides what to say. Everything here is the plumbing: framing, the read loop, and the
    /// two pieces of bookkeeping that make a failure legible when a scripted server misbehaves.
    /// </para>
    /// </remarks>
    private abstract class ScriptedServer(Stream stream)
    {
        /// <summary>
        /// Whatever stopped the loop, or null. The loop runs unobserved, so without this a scripted server
        /// that threw would present as a client that simply never got an answer.
        /// </summary>
        public Exception? Fault { get; private set; }

        /// <summary>Every frame that arrived, as method and id. The other half of the same story.</summary>
        public readonly List<string> Seen = [];

        private int _asked;

        public int TimesAsked => Volatile.Read(ref _asked);

        protected const string Capabilities =
            """
            {"capabilities":{
              "textDocumentSync":{"openClose":true,"change":1},
              "diagnosticProvider":{"interFileDependencies":false,"workspaceDiagnostics":false}}}
            """;

        protected const string FullReport =
            """
            {"kind":"full","items":[
              {"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":9}},
               "message":"`os` imported but unused","severity":2,"source":"hand-written"}]}
            """;

        /// <summary>The same report with a different message, so two answers can be told apart.</summary>
        /// <remarks>
        /// Substituted rather than interpolated: the report's tail is <c>}}]}</c>, and a raw interpolated
        /// literal cannot carry that many consecutive braces as content whatever the dollar count.
        /// </remarks>
        protected static string ReportSaying(string message) =>
            FullReport.Replace("`os` imported but unused", message, StringComparison.Ordinal);

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            try { await LoopAsync(cancellationToken); }
            catch (OperationCanceledException) { /* teardown */ }
            catch (Exception ex) { Fault = ex; }
        }

        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(cancellationToken);
                if (frame is null) return;

                using var document = JsonDocument.Parse(frame);
                var root = document.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                var id = root.TryGetProperty("id", out var i) ? i.GetRawText() : null;
                lock (Seen) Seen.Add($"{method ?? "(a reply to us)"}#{id ?? "-"}");

                if (method == "textDocument/diagnostic") Interlocked.Increment(ref _asked);

                if (await HandleAsync(method, id, cancellationToken)) continue;

                // A METHOD and an id is a request waiting for an answer. An id with no method is the
                // client's answer to us, and replying to THAT makes the client tear the connection down
                // with "a response was received without a request having been sent" — which is the client
                // behaving correctly, and reads as a mysterious hang if you have not seen it before.
                if (method is not null && id is not null) await ResultAsync(id, "null", cancellationToken);
            }
        }

        /// <summary>True when the script dealt with this frame; false to let the default reply stand.</summary>
        protected abstract Task<bool> HandleAsync(string? method, string? id, CancellationToken cancellationToken);

        protected Task ResultAsync(string? id, string result, CancellationToken cancellationToken) =>
            WriteAsync($$"""{"jsonrpc":"2.0","id":{{id}},"result":{{result}}}""", cancellationToken);

        protected async Task WriteAsync(string json, CancellationToken cancellationToken)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(json);
            var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        /// <summary>One LSP frame, or null once the other end has gone.</summary>
        private async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
        {
            // A byte at a time, because the header must not be over-read into the body that follows it.
            var header = new List<byte>();
            var one = new byte[1];
            while (header.Count < 4
                   || header[^4] != (byte)'\r' || header[^3] != (byte)'\n'
                   || header[^2] != (byte)'\r' || header[^1] != (byte)'\n')
            {
                if (await stream.ReadAsync(one, cancellationToken) == 0) return null;
                header.Add(one[0]);
            }

            var text = System.Text.Encoding.ASCII.GetString([.. header]);
            var marker = text.IndexOf("Content-Length:", StringComparison.OrdinalIgnoreCase)
                       + "Content-Length:".Length;
            var length = int.Parse(
                text[marker..].Split('\r')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture);

            var body = new byte[length];
            var read = 0;
            while (read < length)
            {
                var n = await stream.ReadAsync(body.AsMemory(read), cancellationToken);
                if (n == 0) return null;
                read += n;
            }
            return body;
        }
    }

    /// <summary>
    /// A server that sends <c>workspace/diagnostic/refresh</c> with the <c>params: null</c> that rumdl sends.
    /// </summary>
    /// <remarks>
    /// Twice, at two moments that ask different questions: once immediately after the handshake, when
    /// nothing is open — which asks whether the connection survives it — and once after its first
    /// diagnostics answer, which asks whether the refresh is acted on rather than merely tolerated.
    /// </remarks>
    private sealed class HandWrittenServer(Stream stream) : ScriptedServer(stream)
    {
        private int _refreshes;

        /// <summary>A distinct id each time — two live requests may not share one.</summary>
        private string MalformedRefresh() =>
            """{"jsonrpc":"2.0","method":"workspace/diagnostic/refresh","params":null,"id":"""
          + (9000 + Interlocked.Increment(ref _refreshes)) + "}";

        protected override async Task<bool> HandleAsync(
            string? method, string? id, CancellationToken cancellationToken)
        {
            switch (method)
            {
                case "initialize":
                    await ResultAsync(id, Capabilities, cancellationToken);
                    await WriteAsync(MalformedRefresh(), cancellationToken);
                    return true;

                case "textDocument/diagnostic":
                    await ResultAsync(id, FullReport, cancellationToken);

                    // Exactly once, or honouring it would drive the pair round forever.
                    if (TimesAsked == 1) await WriteAsync(MalformedRefresh(), cancellationToken);
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// A server whose refresh lands while an answer is still owed, and which then never answers the request
    /// that refresh provoked.
    /// </summary>
    /// <remarks>
    /// <b>Both halves are ordinary, and together they are the case a naive staleness rule loses.</b> A
    /// refresh arriving mid-flight is routine; a request that is slow or simply never answered is what a
    /// busy or crashed analyser looks like. A client that decides "show only the answer to the newest
    /// request I sent" then has nothing to show and no event that would ever correct it — the document is
    /// blank for good, while the server believes it has reported.
    /// </remarks>
    private sealed class AnswerOvertakenServer(Stream stream) : ScriptedServer(stream)
    {
        protected override async Task<bool> HandleAsync(
            string? method, string? id, CancellationToken cancellationToken)
        {
            switch (method)
            {
                case "initialize":
                    await ResultAsync(id, Capabilities, cancellationToken);
                    return true;

                case "textDocument/diagnostic" when TimesAsked == 1:
                    // The refresh goes out FIRST, so the client asks again before this one is answered.
                    await WriteAsync(
                        """{"jsonrpc":"2.0","method":"workspace/diagnostic/refresh","id":9001}""",
                        cancellationToken);
                    _firstRequestId = id;
                    return true;

                case "textDocument/diagnostic":
                    // The second request is never answered. The first one is, late — and it is the only
                    // answer this document will ever get.
                    await ResultAsync(_firstRequestId, FullReport, cancellationToken);
                    return true;

                default:
                    return false;
            }
        }

        private string? _firstRequestId;
    }

    /// <summary>
    /// A server that keeps its first answer until the document has been closed, and then sends it.
    /// </summary>
    /// <remarks>
    /// An analyser that is merely slow does exactly this, and a user who closes a tab before it finishes is
    /// not doing anything unusual. The interesting part is what the late answer leaves behind: it must not
    /// mark a document nobody has open, and it must not make the next answer for that file — after it is
    /// reopened — look older than itself.
    /// </remarks>
    private sealed class LateAnswerServer(Stream stream) : ScriptedServer(stream)
    {
        private string? _withheld;

        protected override async Task<bool> HandleAsync(
            string? method, string? id, CancellationToken cancellationToken)
        {
            switch (method)
            {
                case "initialize":
                    await ResultAsync(id, Capabilities, cancellationToken);
                    return true;

                case "textDocument/diagnostic" when TimesAsked == 1:
                    _withheld = id;
                    return true;

                case "textDocument/didClose":
                    if (_withheld is { } late)
                    {
                        _withheld = null;
                        await ResultAsync(late, ReportSaying("from before the file was closed"), cancellationToken);
                    }
                    return true;

                case "textDocument/diagnostic":
                    await ResultAsync(id, ReportSaying("from after it was opened again"), cancellationToken);
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// The same shape, except the withheld answer is released on the <b>reopen</b> rather than on the close,
    /// and it is the second request rather than the first.
    /// </summary>
    /// <remarks>
    /// Both details are what make this the case the other test cannot reach. Released on reopen, the late
    /// answer arrives when the document is open again, so refusing to mark a closed document does not catch
    /// it. Withholding the <em>second</em> request gives that answer a higher generation than anything a
    /// reopened document can produce if the counters restart — which is the whole of the bug being guarded.
    /// </remarks>
    private sealed class StaleAcrossReopenServer(Stream stream) : ScriptedServer(stream)
    {
        private string? _withheld;
        private int _opens;

        protected override async Task<bool> HandleAsync(
            string? method, string? id, CancellationToken cancellationToken)
        {
            switch (method)
            {
                case "initialize":
                    await ResultAsync(id, Capabilities, cancellationToken);
                    return true;

                case "textDocument/didOpen" when ++_opens == 2 && _withheld is { } late:
                    // Before the reopen's own request is even read, so the client sees them in this order.
                    _withheld = null;
                    await ResultAsync(late, ReportSaying("from before the file was closed"), cancellationToken);
                    return true;

                case "textDocument/diagnostic" when TimesAsked == 2:
                    _withheld = id;
                    return true;

                case "textDocument/diagnostic":
                    await ResultAsync(id, ReportSaying("from after it was opened again"), cancellationToken);
                    return true;

                default:
                    return false;
            }
        }
    }

    private (VBLspClient Client, Func<TServer> Server) ClientTalkingToAScriptedServer<TServer>(
        Func<Stream, TServer> create)
        where TServer : ScriptedServer
    {
        TServer? server = null;

        var transport = Substitute.For<ILspTransport>();
        transport.IsAlive.Returns(true);
        transport.CanReconnect.Returns(false);
        transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var (clientSide, serverSide) = FullDuplexStream.CreatePair();
                var started = create(serverSide);
                Volatile.Write(ref server, started);
                _ = started.RunAsync(_stopServers.Token);

                return Task.FromResult<IJsonRpcMessageHandler?>(
                    new HeaderDelimitedMessageHandler(
                        clientSide, clientSide, ci.Arg<IJsonRpcMessageFormatter>()));
            });

        // capture: null — the arrangement the byte-level tests above cannot see, and the one that was broken.
        var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), DocumentLanguage.Vb6);
        _clients.Add(client);
        return (client, () => Volatile.Read(ref server)!);
    }

    [Fact]
    public async Task AMalformedFrameDoesNotEndAConnectionNobodyIsRecording()
    {
        // The repair lives in CapturingFormatter, and CapturingFormatter was installed ONLY when a
        // conversation log was supplied. So the connection that survived a malformed frame was the one being
        // watched, and every other one still died — which is the wrong way round: a diagnostic facility must
        // not be what keeps the thing it observes alive. The byte-level test above cannot see this, because
        // it never builds a connection.
        var (sut, server) = ClientTalkingToAScriptedServer(stream => new HandWrittenServer(stream));
        var published = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => { lock (published) published.Add(p); };

        await sut.StartAsync(TestContext.Current.CancellationToken);

        // The malformed frame is already in the pipe — it was written immediately after the handshake reply,
        // so it is read before anything this request could produce. An answer therefore proves survival.
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);

        await Until(() => { lock (published) return published.Count >= 1; },
            "a frame the server should not have sent must cost that message, never the connection",
            () =>
            {
                lock (server().Seen)
                    return $"the scripted server stopped with {server().Fault?.Message ?? "nothing"}, "
                         + $"having seen: {string.Join(", ", server().Seen)}";
            });

        lock (published)
            published[0].Diagnostics.Should().ContainSingle()
                .Which.Message.Should().Contain("imported but unused");
    }

    [Fact]
    public async Task ARefreshRequestIsActuallyActedOn()
    {
        // Surviving the frame and honouring it are different claims, and the first can hide the second:
        // a handler that is never dispatched leaves a healthy connection and no re-request, which looks
        // exactly like success. `workspace/diagnostic/refresh` is the server saying its previous answers are
        // stale — ignoring it leaves the marks wrong until the next keystroke, and possibly forever.
        var (sut, server) = ClientTalkingToAScriptedServer(stream => new HandWrittenServer(stream));

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);

        await Until(() => server().TimesAsked >= 2,
            "the server asked for a refresh after its first answer, so the client must ask again",
            () => { lock (server().Seen) return $"the server saw: {string.Join(", ", server().Seen)}"; });
    }

    [Fact]
    public async Task AnOvertakenAnswerIsStillShownWhenNothingNewerEverArrives()
    {
        // Staleness must be judged against what has been SHOWN, not against what has been ASKED. Judged
        // against the ask, the only answer this server ever sends is discarded — a newer request went out
        // before it came back — and because that newer request is never answered, the document stays blank
        // permanently, with no event that would ever correct it. Judged against what is on screen, the
        // answer is the newest thing to arrive, so it is shown; anything genuinely newer replaces it later.
        var (sut, server) = ClientTalkingToAScriptedServer(stream => new AnswerOvertakenServer(stream));
        var published = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => { lock (published) published.Add(p); };

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);

        await Until(() => { lock (published) return published.Count >= 1; },
            "the one answer the server sent is the best information there is, so it must reach the editor",
            () => { lock (server().Seen) return $"the server saw: {string.Join(", ", server().Seen)}"; });

        lock (published)
            published[^1].Diagnostics.Should().ContainSingle()
                .Which.Message.Should().Contain("imported but unused");
    }

    [Fact]
    public async Task AnAnswerArrivingWhileTheFileIsClosedIsNotShownAtAll()
    {
        // Nothing may be published for a document the editor has let go. The close already cleared it, and
        // a server that only answers when asked has no way to say anything about a file we have stopped
        // asking about — so marks put back afterwards have nothing that could ever remove them.
        var (sut, server) = ClientTalkingToAScriptedServer(stream => new LateAnswerServer(stream));
        var published = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => { lock (published) published.Add(p); };

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Until(() => server().TimesAsked >= 1, "the client should have asked on open");

        // The close is what releases the withheld answer, so this is where the late one is delivered.
        await sut.CloseDocumentAsync(Uri, TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);

        await Until(
            () =>
            {
                lock (published)
                    return published.Count > 0
                        && published[^1].Diagnostics.Any(d => d.Message.Contains("after it was opened again"));
            },
            "the reopened document must show what the server said about it, not what it said before",
            () =>
            {
                lock (published)
                    return "the last thing published was: "
                         + (published.Count == 0
                             ? "nothing at all"
                             : string.Join("; ", published[^1].Diagnostics.Select(d => d.Message)) is { Length: > 0 } m
                                 ? m
                                 : "an empty set");
            });

        lock (published)
            published.Should().NotContain(
                p => p.Diagnostics.Any(d => d.Message.Contains("before the file was closed")),
                "a document nobody has open must not be marked — nothing would ever clear it");
    }

    [Fact]
    public async Task AnAnswerHeldFromBeforeTheCloseDoesNotOutrankTheOneAfterTheReopen()
    {
        // The window the shown-based rule opens if its counters restart on close. An answer still in flight
        // when the document closed carries a higher number than anything a reopened document can produce,
        // so it outranks every fresh answer and the file shows pre-close diagnostics until the counter
        // climbs back past it — two edits, here, and unboundedly many for a file that was busy before.
        // Refusing to mark a closed document does not catch this one: by the time it lands, the document is
        // open again. Keeping the counters monotonic per document is what closes it.
        var (sut, server) = ClientTalkingToAScriptedServer(stream => new StaleAcrossReopenServer(stream));
        var published = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => { lock (published) published.Add(p); };

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Until(() => server().TimesAsked >= 1, "the client should have asked on open");

        // A second ask, so the withheld answer outranks anything a restarted counter could produce.
        await sut.ChangeDocumentAsync(Uri, 2, "import os\n\n", TestContext.Current.CancellationToken);
        await Until(() => server().TimesAsked >= 2, "the client should have asked again after the change");

        await sut.CloseDocumentAsync(Uri, TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);

        await Until(
            () =>
            {
                lock (published)
                    return published[^1].Diagnostics.Any(d => d.Message.Contains("after it was opened again"));
            },
            "the reopened file must settle on what the server said about it, not on what it said before",
            () =>
            {
                lock (published)
                    return "it settled on: "
                         + string.Join("; ", published[^1].Diagnostics.Select(d => d.Message));
            });
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
    /// <param name="extra">
    /// Evaluated only on failure, and only when supplied. A scripted server runs unobserved, so "the thing
    /// never happened" is the same message whether the client ignored something or the server fell over —
    /// this is where the second case gets to say so.
    /// </param>
    private static async Task Until(Func<bool> condition, string what, Func<string>? extra = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
        condition().Should().BeTrue("{0}", extra is null ? what : $"{what} — {extra()}");
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
        var published = new List<PublishDiagnosticsParams>();
        sut.DiagnosticsPublished += (_, p) => { lock (published) published.Add(p); };

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.OpenDocumentAsync(Uri, "import os\n", TestContext.Current.CancellationToken);
        await Until(() => server.TimesAsked >= 1, "the client should have asked on open");
        server.LastPreviousResultId.Should().BeNull("there was no previous answer to refer to");

        // Waiting for the ANSWER, not merely for the question. `TimesAsked` rises the moment the request
        // reaches the server, which is before the client can possibly have been told what the answer is
        // called — so changing the document here raced the reply, and the second request went out with no
        // previous identifier because the client legitimately held none yet. Green on a Windows laptop,
        // red on CI, and the defect was entirely this test's.
        await Until(() => { lock (published) return published.Count >= 1; },
            "the first answer must have arrived before there is a name to refer back to");

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
        // The clients FIRST, and the order is load-bearing. Disposing one sends `shutdown` and waits for an
        // answer, so stopping the scripted servers first leaves it waiting on a server that has stopped
        // listening — and the test hangs in teardown rather than failing, which reads as a build that never
        // finishes rather than as anything to do with this file.
        foreach (var client in _clients)
        {
            try { await client.DisposeAsync(); } catch { /* teardown is best effort */ }
        }

        await _stopServers.CancelAsync();
        _stopServers.Dispose();
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
