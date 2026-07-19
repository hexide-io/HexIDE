using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

public interface ILspClient : IAsyncDisposable
{
    /// <summary>Fired when the server sends textDocument/publishDiagnostics.</summary>
    event EventHandler<PublishDiagnosticsParams>? DiagnosticsPublished;

    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task OpenDocumentAsync(string uri, string text, CancellationToken cancellationToken = default);
    Task ChangeDocumentAsync(string uri, int version, string text, CancellationToken cancellationToken = default);
    Task CloseDocumentAsync(string uri, CancellationToken cancellationToken = default);

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
    Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(string uri, Position position, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/rename and returns the workspace edits, or null.</summary>
    Task<WorkspaceEdit?> RequestRenameAsync(string uri, Position position, string newName, CancellationToken cancellationToken = default);

    /// <summary>Sends textDocument/formatting and returns text edits, or empty.</summary>
    Task<TextEdit[]> RequestFormattingAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Sends vb/builtinSymbols and returns all VBA built-in function signatures, or empty.</summary>
    Task<VbaBuiltinSymbol[]> RequestBuiltinSymbolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Injects diagnostics directly into the pipeline — as if the server had sent a
    /// textDocument/publishDiagnostics notification. Used by external compilers (e.g. VB6.EXE).
    /// Pass an empty array to clear diagnostics for a URI.
    /// </summary>
    Task InjectDiagnosticsAsync(string uri, Diagnostic[] diagnostics);
}
