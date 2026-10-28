using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

public interface ILspClient : IAsyncDisposable
{
    /// <summary>Reports successfully created disk files; the connection applies its server filters.</summary>
    Task DidCreateFilesAsync(string[] uris, CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <summary>Reports successfully renamed disk files, carrying both absolute URIs.</summary>
    Task DidRenameFilesAsync(FileRename[] files, CancellationToken cancellationToken = default) => Task.CompletedTask;
    /// <summary>Reports deleted disk files, never files merely removed from a project.</summary>
    Task DidDeleteFilesAsync(string[] uris, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Rechecks the workspace root and reopens held documents if it moved.</summary>
    Task WorkspaceChangedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Fired when the server sends textDocument/publishDiagnostics.</summary>
    event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;

    /// <summary>
    /// Fired when the server asks for the user's attention about <em>itself</em> — <c>window/showMessage</c>.
    ///
    /// <para>
    /// Distinct from diagnostics, which are about the developer's code. This is the server saying something
    /// is wrong with its own setup, and it is the only channel it has for that. Discarding these is why a
    /// misconfigured server used to be indistinguishable from a broken IDE.
    /// </para>
    ///
    /// <para>
    /// <b>This used to say that <c>window/logMessage</c> deliberately had no event, and that has been
    /// reversed.</b> The reasoning was that running commentary is detail for a log rather than something to
    /// put in front of anyone. That is a sound claim about where it should be <em>shown</em>, and it was
    /// used to decide whether it should be <em>reachable</em>, which is not the same question and not this
    /// interface's to answer. A transport-level contract should hand its consumers what arrived; choosing
    /// a destination is theirs.
    ///
    /// <para>
    /// There was a second argument at the time — that written straight to a logger it landed at a level the
    /// default configuration discards, so a server explaining its own misconfiguration explained it to
    /// nobody. True when written, and deliberately not the reason recorded here, because it is a fact about
    /// one logging configuration rather than about this interface. Make log levels adjustable per logger
    /// and it evaporates; the argument above does not.
    /// </para>
    /// </summary>
    event EventHandler<ShowMessageParams>? MessageShown;

    /// <summary>Work-done progress; sender identifies the originating server through a registry.</summary>
    event EventHandler<ProgressParams>? ProgressReceived;

    /// <summary>
    /// The server's running commentary about itself — <c>window/logMessage</c>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="MessageShown"/> in intent rather than in shape: that one asks for attention,
    /// this one does not. Both are the server talking about its own state rather than about a document, and
    /// a server that starts, connects and then serves nothing has only these to explain itself with.
    ///
    /// <para>
    /// Still logged as well as raised: Log maps to Information, Debug to Trace.
    /// A message nobody sees is one bug and a message with no trace
    /// afterwards is the next one, which is the argument that already applies to its louder sibling.
    /// </para>
    /// </remarks>
    event EventHandler<LogMessageParams>? MessageLogged;

    /// <summary>
    /// What the server says it is doing, when asked to say — <c>$/logTrace</c>.
    /// </summary>
    /// <remarks>
    /// <b>A different kind of information from anything else on this interface.</b> Every other event
    /// reports something that happened to a document or to a connection. This reports the server's own
    /// account of its reasoning, which is the one thing watching the wire cannot reconstruct: a capture
    /// shows that a request took two seconds, and only the server can say that it fell back to a slower
    /// parse to answer it.
    ///
    /// <para>
    /// Arrives only when a trace level was asked for, and in practice only from servers that implement it,
    /// which are fewer than the specification would suggest.
    /// </para>
    /// </remarks>
    event EventHandler<LogTraceParams>? TraceReceived;

    /// <summary>
    /// Fired when this client's liveness changes — connected, disconnected, or reconnected.
    ///
    /// <para>
    /// Without it, a connection's death is observable only by asking. The registry writes a state down at
    /// five points and none of them is a death path, so the far end going away used to be invisible until
    /// something happened to look. A view that only refreshes when the IDE does something else is a view
    /// that reports the past.
    /// </para>
    /// </summary>
    event EventHandler? StateChanged;

    bool IsRunning { get; }

    /// <summary>
    /// What the server called itself during <c>initialize</c>, or null if it said nothing or nothing is
    /// connected. Reported, never trusted: it is the only answer to "which build am I talking to", and it
    /// is the server's own claim about itself.
    /// </summary>
    ServerIdentity? ReportedIdentity { get; }

    /// <summary>
    /// The last attempt to bring this connection up, as a chain with the point it stopped at — or null if
    /// none has been made.
    ///
    /// <para>
    /// Every failure path here converges on the same observable outcome: no connection, and an exception
    /// written to a log nobody is reading. Which of five quite different things happened — the command does
    /// not exist, the pipe had nobody on it, the handshake was never answered, the reply could not be read,
    /// the IDE was simply shutting down — is not recoverable afterwards from anything the client keeps.
    /// </para>
    /// </summary>
    LanguageConnectionAttempt? LastAttempt { get; }

    /// <summary>
    /// Requests this client declined to make because the server did not advertise support for them.
    ///
    /// <para>
    /// Refusing is correct and deliberate. It is also invisible: a feature that is off because the server
    /// never claimed it looks exactly like a feature that is broken, and the refusal is the explanation.
    /// </para>
    /// </summary>
    IReadOnlyList<string> DeclinedCapabilities { get; }

    /// <summary>Records that another claimant has priority for this capability, once per connection.</summary>
    void NotePrioritySkip(string capabilityName, string winnerId) { }

    /// <summary>
    /// What the server advertised during initialize, exactly as it sent it — or null if nothing is
    /// connected, or the reply could not be read.
    ///
    /// <para>
    /// Raw rather than reduced to a summary, deliberately. Any summary invented now will be wrong for a
    /// server not yet met, and the unedited answer is the only honest response to "why is this feature
    /// unavailable here". It is also what a connections view should show.
    /// </para>
    /// </summary>
    System.Text.Json.JsonElement? AdvertisedCapabilities { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    /// <summary>Opens a document at <see cref="LspDocumentVersion.Opening"/>.</summary>
    Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a document at <see cref="LspDocumentVersion.Opening"/>, stating whether it belongs to a VB6
    /// project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A project member is known to be VB6 whatever its extension shares with another language, so it is
    /// offered only to servers whose own claim establishes VB6 (hexide-io/HexIDE#279). <c>.cls</c> is the
    /// case that matters: a VB6 class module and a LaTeX class file spell the same.
    /// </para>
    /// <para>
    /// <b>An overload, not a defaulted parameter on the method above.</b> A <c>bool</c> defaulted after the
    /// cancellation token would compile at every existing call site and then quietly change what they mean:
    /// every <c>Received().OpenDocumentAsync(uri, text, Arg.Any&lt;CancellationToken&gt;())</c> in the suite
    /// would start asserting against an implicit <c>false</c> that production no longer passes, and the
    /// resulting wall of red reads as a routing regression rather than as a signature change.
    /// </para>
    /// <para>
    /// The caller states it because the registry cannot work it out. It holds no model of the project by
    /// design, and neither parsing an <c>untitled:</c> name back into its parts nor matching a path would
    /// answer — the first is forbidden everywhere else in this seam, and the second cannot see a document
    /// that has no file. The opener already knows.
    /// </para>
    /// </remarks>
    Task OpenDocumentAsync(
        string uri, string text, bool isProjectMember, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a document's whole new text. <paramref name="version"/> must be later than the version of the
    /// last message about the document, beginning with <see cref="LspDocumentVersion.Opening"/>, or a server
    /// that orders by version may discard the text as stale.
    /// </summary>
    Task ChangeDocumentAsync(string uri, int version, string text, CancellationToken cancellationToken = default);
    Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a document because it is about to be re-opened under a different name, and withdraws every
    /// diagnostic recorded under the old one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="CloseDocumentAsync"/> because the two want opposite things. An ordinary
    /// close must NOT withdraw everything: a build's claim about a form outlives the editor that happened
    /// to be showing it, and the document still exists under that name. A rename must, because nothing
    /// will ever answer to the old name again, so anything left under it is a mark with no way to be
    /// cleared — which is hexide-io/HexIDE#269's shape arriving by a different route.
    /// </para>
    /// <para>
    /// Not expressible as "close, then publish an empty set". An empty publication from a language server
    /// removes only that server's rows; a document carrying an injected compiler diagnostic as well still
    /// publishes a non-empty union, and those survivors are exactly the ones nothing can clear.
    /// </para>
    /// </remarks>
    Task CloseDocumentForRenameAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the servers holding this document that it has been written to disk.
    ///
    /// <para>
    /// No text parameter: whether the text accompanies the notification is negotiated per connection, and
    /// the connection already holds what it last sent. A caller supplying text would either duplicate that
    /// state or override a negotiation it cannot see the result of.
    /// </para>
    ///
    /// <para>
    /// A server that did not ask for save notifications is not sent one, and that is not an error. Most
    /// servers do not ask, because most re-analyse on every change and have nothing left to do at save.
    /// </para>
    /// </summary>
    Task SaveDocumentAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/hover and returns the result, or null if there is nothing to show.</summary>
    Task<HoverResult?> RequestHoverAsync(string uri, Position position, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/documentSymbol and returns the list of symbols, or empty.</summary>
    Task<DocumentSymbol[]> RequestDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/foldingRange and returns the list of fold ranges, or empty.</summary>
    Task<SemanticTokenResult?> RequestSemanticTokensAsync(string uri, CancellationToken cancellationToken = default);

    Task<FoldingRange[]> RequestFoldingRangesAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/completion and returns completion items, or empty.</summary>
    Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position, CancellationToken cancellationToken = default);

    /// <summary>Calls onAnswer only when completion was answered, including a valid empty answer.</summary>
    async Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position,
        CancellationToken cancellationToken, Action onAnswer)
    {
        var items = await RequestCompletionAsync(uri, position, cancellationToken);
        onAnswer();
        return items;
    }

    /// <summary>The completion triggers advertised for this document.</summary>
    IReadOnlyList<string> CompletionTriggerCharacters(string uri) => [];

    /// <summary>Requests completion only from claimants advertising this trigger.</summary>
    Task<CompletionItem[]> RequestCompletionForTriggerAsync(string uri, Position position,
        string triggerCharacter, CancellationToken cancellationToken = default) =>
        RequestCompletionAsync(uri, position, cancellationToken);

    /// <summary>Records a completion answer discarded after the popup's result was returned.</summary>
    void NoteLateCompletion() { }

    /// <summary>Sends textDocument/signatureHelp and returns signature information, or null.
    /// A typed trigger selects advertising servers in the registry; null means explicit invocation.</summary>
    Task<SignatureHelp?> RequestSignatureHelpAsync(string uri, Position position, CancellationToken cancellationToken = default, string? triggerCharacter = null);
    Task<Location[]?> RequestDefinitionAsync(string uri, Position position, CancellationToken cancellationToken = default);

    /// <summary>
    /// Where the symbol at <paramref name="position"/> is DECLARED, which is not always where it is defined.
    /// </summary>
    /// <remarks>
    /// A separate request because the two are separate questions, and a server that distinguishes them
    /// answers each differently: a C header's declaration against its .cpp definition is the clearest case,
    /// and VB6 has its own — a <c>Declare Function</c> or an <c>Implements</c> member's signature is the
    /// declaration, while the body that runs is the definition. A client that only asks for the definition
    /// cannot reach the other one at all.
    /// </remarks>
    Task<Location[]?> RequestDeclarationAsync(string uri, Position position, CancellationToken cancellationToken = default);
    Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(string uri, Position position, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/rename and returns edits, a refusal, or null for no change.</summary>
    Task<RenameResult?> RequestRenameAsync(string uri, Position position, string newName, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/formatting and returns text edits, or empty.</summary>
    Task<TextEdit[]> RequestFormattingAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends <c>textDocument/codeLens</c> and returns the lenses, or empty.
    /// </summary>
    /// <remarks>
    /// <b>Every lens that comes back is resolved.</b> The protocol lets a server return a lens with no
    /// command and expect a <c>codeLens/resolve</c> round trip, and an unresolved lens is one the user can
    /// see and cannot click — the worst of the three states. An implementation therefore resolves its own
    /// before answering, which keeps the resolve on the connection that issued the lens without any caller
    /// having to know that lenses have provenance.
    /// </remarks>
    Task<CodeLens[]> RequestCodeLensesAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Notifies opted-in servers of workspace folder changes before project import.</summary>
    Task UpdateWorkspaceFoldersAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Imports a project and refreshes diagnostics. Failures propagate to configuration reporting.</summary>
    Task<System.Text.Json.JsonElement?> ImportProjectAsync(ProjectImportSpec spec, string uri, string text,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("Project import is unavailable.");

    /// <summary>
    /// Sends <c>workspace/executeCommand</c> and returns whatever the server answered, or null.
    /// </summary>
    /// <remarks>
    /// <b>Not document-scoped, and routed differently because of it.</b> The command goes to the server that
    /// declared it in <c>executeCommandProvider.commands</c> — see
    /// <see cref="ServerCapabilities.DeclaresCommand"/>. A command no started server declares is not sent
    /// anywhere and returns null, which is the only safe answer: guessing would run a command on a server
    /// that never offered it.
    /// </remarks>
    Task<System.Text.Json.JsonElement?> ExecuteCommandAsync(
        string command,
        System.Text.Json.JsonElement[]? arguments = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends vb/builtinSymbols and returns all VBA built-in function signatures, or empty.</summary>
    Task<VbaBuiltinSymbol[]> RequestBuiltinSymbolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Symbols matching <paramref name="query"/> anywhere in the workspace, or empty.
    /// </summary>
    /// <remarks>
    /// <b>The first request here with no document.</b> Every other language request is about a file, which
    /// is what decides the server; this one is about the workspace, so it is routed by advertised
    /// capability instead — and needs the workspace to have been described, which is why it depends on
    /// <c>workspaceFolders</c> reaching <c>initialize</c>.
    /// </remarks>
    Task<SymbolInformation[]> RequestWorkspaceSymbolsAsync(
        string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the server to change how much it says about its own work, without restarting it.
    /// </summary>
    /// <remarks>
    /// <b>Fire-and-forget, and unverifiable.</b> <c>$/setTrace</c> is a notification, so there is no reply,
    /// no error, and no capability by which a client can discover whether a server honours tracing at all.
    /// Measured against five servers, every one accepted the notification and four then emitted nothing
    /// ever. So a caller may report that a server was asked; it may never report that a server agreed.
    ///
    /// <para>
    /// The level a server <em>starts</em> at is not set through here. That one travels in the initialization
    /// request and so must be known before the process exists, which is why it comes from configuration.
    /// </para>
    ///
    /// <para>
    /// On a router this reaches every running connection. Setting the level for one named server is a
    /// different operation on a different interface, and belongs there because this one deliberately has no
    /// notion of which server is answering.
    /// </para>
    /// </remarks>
    Task SetTraceAsync(string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects diagnostics directly into the pipeline — as if the server had sent a
    /// textDocument/publishDiagnostics notification. Used by external compilers (e.g. VB6.EXE).
    /// </summary>
    /// <param name="uri">The document the diagnostics are about.</param>
    /// <param name="diagnostics">What this source now says about that document; empty says nothing.</param>
    /// <param name="owner">
    /// Who is saying it — see <see cref="DiagnosticOwner"/>. Required rather than defaulted: an
    /// unattributed publish is what made a build erase the language server's marks (#358), and the
    /// parameter exists so that cannot be reintroduced by omission.
    /// </param>
    /// <remarks>
    /// <b>This replaces only what <paramref name="owner"/> last said about the document, not the
    /// document's diagnostics.</b> What subscribers receive is still one whole-document set — the union of
    /// every source's current claim — so nothing downstream has to reason about who published what.
    /// </remarks>
    Task InjectDiagnosticsAsync(string uri, Diagnostic[] diagnostics, string owner);

    /// <summary>
    /// Withdraws everything one source has published, across every document, and republishes what remains.
    ///
    /// <para>
    /// This is how an injecting source expires its own previous run — a build clearing the errors of the
    /// build before it. Scoped by what that source actually published rather than by a list of documents
    /// the caller assembles, because the two disagree exactly when it matters: a form renamed since the
    /// last build is absent from the caller's list and still carries the marks.
    /// </para>
    /// </summary>
    Task ClearDiagnosticsFromAsync(string owner);
}
