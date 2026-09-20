using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using HexIDE.Conversations;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What five real servers do with a document that has no file yet.
///
/// <para>
/// hexide-io/HexIDE#273 names such a document <c>untitled:&lt;Project&gt;/&lt;Name&gt;.&lt;ext&gt;</c>. Every
/// URI HexIDE has ever sent has been <c>vb6://</c> or <c>file:</c>, so whether a server it did not write
/// will answer about a third scheme at all is a question about other people's code, and the only way to
/// answer it is to ask them.
/// </para>
///
/// <para>
/// <b>Each server is asked with its own extension, and that is not a shortcut.</b> Routing is by extension,
/// so <c>untitled:Project1/Module1.bas</c> reaches none of these five — they claim Markdown, LaTeX, JSON,
/// C++ and Python. What is under test is the <em>scheme</em>: whether a server accepts, holds and answers
/// about a document whose URI names no file. Sending each one an extension it actually claims is what
/// isolates that question from routing, and a VB6 extension would answer a different one — whether these
/// servers claim <c>.bas</c>, which they do not and should not.
/// </para>
///
/// <para>
/// <b>Every assertion here reads a frame, and the first draft did not.</b> It asserted
/// <c>PublishDiagnosticsParams.Uri</c> against the name that was sent — which is a real echo from a server
/// that publishes, and a <em>tautology</em> for one that answers when asked: an LSP
/// <c>DocumentDiagnosticReport</c> carries no URI at all, so <c>ApplyDiagnosticReport</c> raises the event
/// with the client's own string (<c>VBLspClient.cs:893-918</c>). Three of these five deliver that way, so
/// three of the tests were comparing HexIDE with HexIDE and would have passed had the server re-spelled the
/// name, or never been told one. The bodies are armed and read instead, which is the only place the
/// server's own bytes exist.
/// </para>
///
/// <para>
/// Measured 2026-09-20; the table is in the change's design record. Four of the five accept the scheme,
/// one refuses it outright, and one drops a name that is not percent-encoded without a word.
/// </para>
/// </summary>
public class UntitledDocumentNamesTests : IAsyncDisposable
{
    private readonly List<LspClientRegistry> _registries = [];
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => { });
    private readonly ConversationLog _capture = new();

    // Documents with an obvious, uncontroversial defect in each language. Which rule fires is the server's
    // business; that SOMETHING is reported is what proves it read the text it was handed.
    private const string SloppyMarkdown = "##Heading without a space\n\n\n\nsome text\n\n\n";
    private const string BrokenLatex =
        "\\documentclass{article}\n\\begin{document}\n\\begin{itemize}\n\\end{document}\n";
    private const string InvalidJson = """{ "a": 1, }""";
    private const string PythonWithAnUnusedImport = "import os\n\n\ndef greet(name):\n    return name\n";
    private const string CppWithAnUndeclaredName = "int main() { return x; }\n";

    private LspClientRegistry RegistryFor(ForeignServer server, string id)
    {
        // Armed before the connection exists, because the frames these tests read are the ones that carry a
        // URI, and only the opening handful are kept unasked.
        _capture.Arm(id, armed: true);

        var info = new LspServerInfo(server.Find()!, server.LaunchArguments, Path.GetTempPath());
        var registration = new LanguageServerRegistration(
            Id: id,
            DisplayName: id,
            Extensions: server.Extensions,
            LanguageId: server.LanguageId,
            CreateClient: () => new VBLspClient(
                new StdioProcessLspTransport(info, _loggerFactory.CreateLogger<StdioProcessLspTransport>()),
                _loggerFactory.CreateLogger<VBLspClient>(),
                server.LanguageId,
                capture: _capture, connectionId: id));

        var registry = new LspClientRegistry([registration], _loggerFactory.CreateLogger<LspClientRegistry>());
        _registries.Add(registry);
        return registry;
    }

    /// <summary>What one server did when it was handed one document: everything a test here needs to say so.</summary>
    private sealed record Attempt(
        LspClientRegistry Registry,
        PublishDiagnosticsParams? Published,
        LanguageServerConnection Connection,
        TimeSpan Took);

    /// <summary>
    /// Opens one document under a name of this change's shape and waits for the server to say something
    /// about it. Answers what arrived, how long it took, and the connection it arrived on.
    /// </summary>
    private async Task<Attempt> OpenUnderUntitledName(
        ForeignServer server, string id, string uri, string text, TimeSpan wait)
    {
        var registry = RegistryFor(server, id);
        var received = new TaskCompletionSource<PublishDiagnosticsParams>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // Non-empty only. A server may report an empty set for a document it has not analysed yet, and
        // taking that as the answer would let this pass for a server that never read the text.
        registry.DiagnosticsPublished += (_, p) => { if (p.Diagnostics.Length > 0) received.TrySetResult(p); };

        var started = Stopwatch.StartNew();
        await registry.OpenDocumentAsync(uri, text);

        PublishDiagnosticsParams? published = null;
        try { published = await received.Task.WaitAsync(wait); }
        catch (TimeoutException) { /* the caller decides whether silence is the finding or the failure */ }

        started.Stop();
        await _capture.DrainAsync();
        return new Attempt(registry, published, registry.Connections.Single(), started.Elapsed);
    }

    // ── Reading what actually crossed the wire ────────────────────────────────────────────────────────

    private JsonElement Frame(string id, long sequence)
    {
        var body = _capture.Body(id, sequence);
        body.Should().NotBeNull(
            $"sequence {sequence} on '{id}' has no retained body, so nothing here can be read off the wire "
          + "— the capture must be armed before the connection is made");
        body!.IsTruncated.Should().BeFalse("a truncated frame cannot be parsed, and these are small");
        return JsonDocument.Parse(Encoding.UTF8.GetString(body.Head)).RootElement;
    }

    /// <summary>The <c>textDocument.uri</c> a frame named, read out of its bytes.</summary>
    private string DocumentUriIn(string id, long sequence)
    {
        var frame = Frame(id, sequence);
        frame.TryGetProperty("params", out var p).Should().BeTrue("every frame read here carries params");
        // publishDiagnostics puts the uri directly on params; didOpen and the pull request nest it under
        // textDocument. One reader for both, because the question is the same.
        if (p.TryGetProperty("textDocument", out var document)
            && document.TryGetProperty("uri", out var nested))
            return nested.GetString()!;
        return p.GetProperty("uri").GetString()!;
    }

    private ConversationEnvelope TheOnly(string id, string method, ConversationDirection direction)
    {
        var matches = _capture.Snapshot(id)
            .Where(e => e.Method == method && e.Direction == direction
                        && e.Kind is ConversationEntryKind.Request or ConversationEntryKind.Notification)
            .ToList();
        matches.Should().NotBeEmpty($"'{method}' never crossed this connection. {Conversation(id)}");
        return matches[0];
    }

    /// <summary>
    /// True once the record holds a matching entry, draining as it goes. For anything written from a
    /// different thread than the frame it belongs to, which a single drain can split.
    /// </summary>
    private async Task<bool> WaitFor(string id, Func<ConversationEnvelope, bool> match, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        while (true)
        {
            await _capture.DrainAsync();
            if (_capture.Snapshot(id).Any(match)) return true;
            if (DateTime.UtcNow >= deadline) return false;
            await Task.Delay(50);
        }
    }

    private string Conversation(string id) =>
        "The conversation was: " + string.Join(" | ", _capture.Snapshot(id)
            .Select(e => $"{e.Sequence} {e.Direction} {e.Kind} {e.Method} {e.Outcome} {e.Detail}".Trim()));

    // ── The shared proof ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For a server that tolerates the scheme: HexIDE put the name on the wire unchanged, and the server
    /// produced something about <em>that</em> document.
    /// </summary>
    private async Task AssertItAnswersUnderTheNameItWasGiven(
        ForeignServer server, string id, string extension, string text)
    {
        var uri = $"untitled:Project1/Module1{extension}";
        var (_, published, connection, _) =
            await OpenUnderUntitledName(server, id, uri, text, TimeSpan.FromSeconds(30));

        connection.State.Should().Be(LanguageConnectionState.Running,
            "{0}", ConnectionDiagnostics.Explain(connection, _capture));

        // 1. The name left HexIDE intact. Read off the sent frame rather than trusted: the client could
        //    normalise, re-encode or lower-case a scheme on the way out, and every later assertion would
        //    still hold against whatever it sent instead.
        DocumentUriIn(id, TheOnly(id, "textDocument/didOpen", ConversationDirection.Sent).Sequence)
            .Should().Be(uri, "the URI on the wire is the name the document actually has");

        published.Should().NotBeNull(
            "a server that holds the document says something about it; nothing arrived in 30s. " + Conversation(id));

        published!.Diagnostics.Should().OnlyContain(
            d => d.Range.Start.Line >= 0 && d.Range.End.Line >= d.Range.Start.Line,
            "ranges must be well-formed, or the editor cannot place a marker");

        // 2. Which half applies is decided by what the SERVER declared, never by a list written here. A
        //    pull server publishes nothing at all, so a test that waited for a publication would pass
        //    vacuously against it; a push server is never asked, so requiring the request would fail
        //    against it.
        var pull = connection.Capabilities is { } caps && caps.TryGetProperty("diagnosticProvider", out _);
        var wire = _capture.Snapshot(id);

        if (pull)
        {
            // The request names the document, and the answer to THAT request carried findings. That chain
            // is what makes this a measurement of the server: a `DocumentDiagnosticReport` carries no URI,
            // so the server cannot echo a name and nothing here pretends it did — what it must do is
            // resolve the name to text it is holding, and a report with items in it is the only way it
            // could. Asked about a document it never took, these servers answer empty or not at all.
            // Selected by having been answered, rather than taken as the first one sent. One of these
            // servers re-pulls, so there is more than one request on the wire and the later one may still
            // be outstanding when this runs — picking by position would make the assertion depend on how
            // many times the server changed its mind.
            var request = wire.FirstOrDefault(
                e => e.Method == "textDocument/diagnostic" && e.Direction == ConversationDirection.Sent
                     && e.Outcome == ConversationOutcome.Answered && e.AnswerSequence is not null);
            request.Should().NotBeNull(
                "this server advertised diagnosticProvider, so nothing reaches the editor unless HexIDE "
              + "asked and it answered. " + Conversation(id));
            DocumentUriIn(id, request!.Sequence).Should().Be(uri, "and it was asked about this document");

            var report = Frame(id, request.AnswerSequence!.Value);
            report.GetProperty("result").GetProperty("items").GetArrayLength()
                .Should().BeGreaterThan(0,
                    "the answer to that request carried findings, which the server could only produce from "
                  + "text it is holding under that name");

            // A server may publish as well as answer, and one of these does. What it must not do is have
            // both taken: two whole-document sets under one owner leaves the marks depending on which
            // landed last. The client notes the first one it drops — once per connection, by design — so
            // this looks for one note, not one per publication.
            //
            // Asked in this order, and not the other way round, because the two entries are written by
            // different threads: the publication is recorded on the read loop as it is deserialized, and
            // the note is queued afterwards from the handler that dropped it. "If a publication arrived,
            // assert the note" therefore races a drain landing between them — sometimes a spurious
            // failure, sometimes an assertion that quietly does not run. Waiting for the note and only
            // then requiring that nothing was published has neither failure mode.
            var noted = await WaitFor(id, e => e.Method == "textDocument/publishDiagnostics"
                                               && e.Kind == ConversationEntryKind.Unconsumed
                                               && e.Detail != null
                                               && e.Detail.StartsWith("ignored:", StringComparison.Ordinal),
                                      TimeSpan.FromSeconds(5));
            if (!noted)
            {
                _capture.Snapshot(id).Should().NotContain(
                    e => e.Method == "textDocument/publishDiagnostics"
                         && e.Direction == ConversationDirection.Received
                         && e.Kind == ConversationEntryKind.Notification,
                    "this server published as well as answering, and the publication was taken rather than "
                  + "dropped — which puts two whole-document sets under one owner. " + Conversation(id));
            }
        }
        else
        {
            // Here the name genuinely does come back: `publishDiagnostics` carries the server's own URI, so
            // this is the one place in these tests where a byte-for-byte echo is a fact about the server.
            var publication = TheOnly(id, "textDocument/publishDiagnostics", ConversationDirection.Received);
            DocumentUriIn(id, publication.Sequence).Should().Be(uri,
                "this server publishes, so it spells the name back, and it must spell it the way it was "
              + "given — an editor keys its markers on the string it sent");

            // And it was not asked. Requesting a method a server never advertised is the shape of #242 and
            // #267, and it is also what makes this branch discriminating: without it, a server that both
            // advertises a provider and publishes satisfies either branch, so getting the branch backwards
            // would go unnoticed. Measured: inverting the branch left one server's test green until this
            // was added.
            wire.Should().NotContain(
                e => e.Method == "textDocument/diagnostic",
                "a server that did not advertise the pull model must not be asked to answer by it. "
              + Conversation(id));
        }

        // 3. HexIDE's own bookkeeping, which is a different claim from either branch above and worth its
        //    own line: whatever the delivery model, the marks are filed under the name that was sent.
        published.Uri.Should().Be(uri, "the diagnostics are filed under the name the document was opened as");
    }

    // ── The four that tolerate the scheme ─────────────────────────────────────────────────────────────

    [ForeignServerFact]
    public Task AMarkdownServerAnswersAboutADocumentWithNoFile() =>
        AssertItAnswersUnderTheNameItWasGiven(ForeignServer.Markdown, "untitled.markdown", ".md", SloppyMarkdown);

    [ForeignServerFact("latex")]
    public Task ALatexServerAnswersAboutADocumentWithNoFile() =>
        AssertItAnswersUnderTheNameItWasGiven(ForeignServer.Latex, "untitled.latex", ".tex", BrokenLatex);

    [ForeignServerFact("json")]
    public Task TheReferenceImplementationAnswersAboutADocumentWithNoFile() =>
        AssertItAnswersUnderTheNameItWasGiven(ForeignServer.Json, "untitled.json", ".json", InvalidJson);

    [ForeignServerFact("python")]
    public Task APullModelServerAnswersAboutADocumentWithNoFile() =>
        AssertItAnswersUnderTheNameItWasGiven(ForeignServer.Python, "untitled.python", ".py", PythonWithAnUnusedImport);

    // ── The one that refuses ──────────────────────────────────────────────────────────────────────────

    [ForeignServerFact("cpp")]
    public async Task TheServerThatAcceptsOnlyFileUrisSaysSoOnStandardError()
    {
        // The refusal is asserted from what the server SAID, not from what failed to arrive. "No
        // diagnostics" is what a server that is merely slow, or busy, or uninterested looks like, and this
        // suite's recurring failure is an absence read as a result.
        //
        // It refuses a notification, so there is no response to carry an error and no request to fail:
        // standard error is the only channel the refusal can take, which is exactly why the capture records
        // it alongside the messages.
        const string id = "untitled.cpp";
        var uri = "untitled:Project1/Module1.cpp";
        var (_, published, connection, _) = await OpenUnderUntitledName(
            ForeignServer.Cpp, id, uri, CppWithAnUndeclaredName, TimeSpan.FromSeconds(15));

        connection.State.Should().Be(LanguageConnectionState.Running,
            "the connection itself is healthy — it is the document this server will not take. {0}",
            ConnectionDiagnostics.Explain(connection, _capture));

        DocumentUriIn(id, TheOnly(id, "textDocument/didOpen", ConversationDirection.Sent).Sequence)
            .Should().Be(uri, "it was offered the document before it refused it");

        var stderr = string.Join("\n", _capture.Snapshot(id)
            .Where(e => e.Kind == ConversationEntryKind.StandardError)
            .Select(e => e.Detail));

        stderr.Should().Contain("only supports 'file' URI scheme",
            "this server states its refusal, and reading it is the difference between a measurement and a "
          + "guess. " + Conversation(id));
        stderr.Should().Contain("textDocument/didOpen",
            "and it names the notification it threw away, so the document was never opened at all");

        published.Should().BeNull(
            "a document it refused to decode cannot be one it reports on — this is the CONSEQUENCE of the "
          + "refusal above, and on its own it would prove nothing");
    }

    // ── The name that has to be encoded ───────────────────────────────────────────────────────────────

    [ForeignServerFact("latex")]
    public async Task ANameThatIsNotPercentEncodedIsDroppedWhereItsEncodedFormIsAnswered()
    {
        // WHY the change mints these through the URI type instead of interpolating a string, measured on the
        // server that punishes the shortcut. A VB6 project or module may be named in any script the user's
        // machine allows, so this is not an exotic case — it is what happens the first time someone works in
        // their own language.
        //
        // The two halves are one test on purpose. Either alone is weak: the negative is an absence, and the
        // positive says nothing about what the absence means. Together they isolate the single variable —
        // same server, same document text, same extension, only the spelling of the name differs.
        const string raw = "untitled:Prøjekt/Модуль.tex";
        const string encoded = "untitled:Pr%C3%B8jekt/%D0%9C%D0%BE%D0%B4%D1%83%D0%BB%D1%8C.tex";
        const string encodedId = "untitled.latex.encoded";
        const string rawId = "untitled.latex.raw";

        var (_, answered, _, took) = await OpenUnderUntitledName(
            ForeignServer.Latex, encodedId, encoded, BrokenLatex, TimeSpan.FromSeconds(30));

        answered.Should().NotBeNull("the encoded form is a valid URI and this server answers about it");
        DocumentUriIn(encodedId,
                TheOnly(encodedId, "textDocument/publishDiagnostics", ConversationDirection.Received).Sequence)
            .Should().Be(encoded, "and it hands the encoded name back exactly as it received it");

        // Self-calibrating rather than a guessed constant. The negative half has to distinguish "dropped"
        // from "slower than the number somebody typed here", so it waits a large multiple of what this very
        // server just took to answer the same document on the same machine — which on a loaded CI runner
        // scales with the load rather than against it.
        var patience = TimeSpan.FromSeconds(Math.Max(15, took.TotalSeconds * 8));

        var (registryForRaw, dropped, connection, _) = await OpenUnderUntitledName(
            ForeignServer.Latex, rawId, raw, BrokenLatex, patience);

        connection.State.Should().Be(LanguageConnectionState.Running,
            "the server is alive and the connection is up — it simply never answers. {0}",
            ConnectionDiagnostics.Explain(connection, _capture));

        // The absences below are only worth anything if this connection is in the record at all: Snapshot
        // answers an empty list for an id it has never seen, which would make every NotContain pass by
        // naming nothing. So the notification is established as present, and as carrying the raw name,
        // before anything is asserted to be missing.
        DocumentUriIn(rawId, TheOnly(rawId, "textDocument/didOpen", ConversationDirection.Sent).Sequence)
            .Should().Be(raw, "the raw name did reach the wire, so what follows is about the server");

        dropped.Should().BeNull(
            $"this server drops a notification whose URI is not strictly valid, and says nothing: no "
          + $"response, no error, no standard error. It answered the encoded name in {took.TotalSeconds:0.0}s "
          + $"and stayed silent for {patience.TotalSeconds:0}s on the raw one. " + Conversation(rawId));

        _capture.Snapshot(rawId)
            .Should().NotContain(e => e.Kind == ConversationEntryKind.StandardError,
                "and it does not complain either, which is what makes this the dangerous one: a client that "
              + "interpolated the name would see a document that simply never gets diagnostics, with nothing "
              + "anywhere to say why");

        // And the cost is not one lost notification. A request naming the document the server threw away is
        // never answered — the wire records it sent, with no outcome, indefinitely. Only initialize is
        // bounded by a timeout (hexide-io/HexIDE#486), so the caller's await would wait for the process to
        // exit. The wait here is this test's own, not the client's.
        var request = registryForRaw.RequestFoldingRangesAsync(raw);
        var whicheverFinished = await Task.WhenAny(request, Task.Delay(patience));

        whicheverFinished.Should().NotBeSameAs(request,
            "a request about a document this server silently discarded is never answered, and nothing in "
          + "the client gives up on it. " + Conversation(rawId));

        await _capture.DrainAsync();
        _capture.Snapshot(rawId)
            .Should().Contain(
                e => e.Method == "textDocument/foldingRange" && e.Direction == ConversationDirection.Sent
                     && e.Outcome == ConversationOutcome.None,
                "and it is on the wire with no outcome, which is what 'hanging' looks like from here");
    }

    // ── The delivery model, re-measured under this client ─────────────────────────────────────────────

    [ForeignServerFact("python")]
    public async Task RefusingDynamicRegistrationDoesNotTalkThisServerOutOfAnswering()
    {
        // This server decides how to deliver diagnostics from what the client declared, and the probe that
        // first measured it accepted dynamic registration where HexIDE refuses it — so its measured mode
        // was the probe's, not HexIDE's. Re-measured here against the client that ships, and under a name
        // with no file behind it, which is the combination nothing had asked about.
        const string id = "untitled.python.dynreg";
        var uri = "untitled:Project1/Module1.py";
        var (_, published, connection, _) = await OpenUnderUntitledName(
            ForeignServer.Python, id, uri, PythonWithAnUnusedImport, TimeSpan.FromSeconds(30));

        var stderr = string.Join("\n", _capture.Snapshot(id)
            .Where(e => e.Kind == ConversationEntryKind.StandardError)
            .Select(e => e.Detail));

        stderr.Should().Contain("dynamic capability registration",
            "HexIDE declines dynamic registration, and this server notices and says so — which is the "
          + "condition under which the rest of this test is a measurement of HexIDE rather than of a probe. "
          + Conversation(id));

        published.Should().NotBeNull("it still answers, by the pull model, for a document with no file");
        published!.Diagnostics.Should().Contain(
            d => d.CodeText == "F401",
            "the document's only defect is an unused import, and naming the rule is what stops this passing "
          + "for an unrelated complaint — it is also content this server could only produce from the text "
          + "sent under this name");

        var answeredPull = _capture.Snapshot(id).FirstOrDefault(
            e => e.Method == "textDocument/diagnostic" && e.Direction == ConversationDirection.Sent
                 && e.Outcome == ConversationOutcome.Answered && e.AnswerSequence is not null);
        answeredPull.Should().NotBeNull("the findings above came back as an answer, so there is one");
        DocumentUriIn(id, answeredPull!.Sequence)
            .Should().Be(uri, "and the request that produced them named the untitled document");

        connection.Capabilities.Should().NotBeNull();
        connection.Capabilities!.Value.TryGetProperty("diagnosticProvider", out _).Should().BeTrue(
            "and it is still the pull model it advertised, not a fallback to publishing");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var registry in _registries)
        {
            try { await registry.DisposeAsync(); } catch { /* teardown is best effort */ }
        }
        await _capture.DisposeAsync();
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
