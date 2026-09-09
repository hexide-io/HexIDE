using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

public interface ILspClient : IAsyncDisposable
{
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
    /// <c>window/logMessage</c> deliberately has no event: it is diagnostic detail for a log, not something
    /// to put in front of anyone, and it is written straight to the logger.
    /// </para>
    /// </summary>
    event EventHandler<ShowMessageParams>? MessageShown;

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
    Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default);
    Task ChangeDocumentAsync(string uri, int version, string text, CancellationToken cancellationToken = default);
    Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default);

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
    Task<FoldingRange[]> RequestFoldingRangesAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/completion and returns completion items, or empty.</summary>
    Task<CompletionItem[]> RequestCompletionAsync(string uri, Position position, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/signatureHelp and returns signature information, or null.</summary>
    Task<SignatureHelp?> RequestSignatureHelpAsync(string uri, Position position, CancellationToken cancellationToken = default);
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

    /// <summary>Sends textDocument/rename and returns the workspace edits, or null.</summary>
    Task<WorkspaceEdit?> RequestRenameAsync(string uri, Position position, string newName, CancellationToken cancellationToken = default);

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
    /// Injects diagnostics directly into the pipeline — as if the server had sent a
    /// textDocument/publishDiagnostics notification. Used by external compilers (e.g. VB6.EXE).
    /// Pass an empty array to clear diagnostics for a URI.
    /// </summary>
    Task InjectDiagnosticsAsync(string uri, Diagnostic[] diagnostics);
}
