// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Per-URI document cache for the LSP server. Written only from the sequential notification handlers;
// the parse runs off-thread (see UpdateDocumentAsync) but the SingleThreadScheduler awaits each handler
// to completion, so cache access is never concurrent — no locking required.

namespace HexIDE.VbLspServer;

/// <summary>
/// Holds per-document analysis results, refreshed once per didOpen/didChange. Request handlers read
/// from these caches; the notification handlers write via <see cref="UpdateDocumentAsync"/> /
/// <see cref="RemoveDocument"/>.
/// </summary>
internal sealed class DocumentStore
{
    public Dictionary<string, string> Source { get; } = new();
    public Dictionary<string, List<LspDiagnostic>> Diagnostics { get; } = new();
    public Dictionary<string, List<LspSymbol>> Symbols { get; } = new();
    public Dictionary<string, List<LspFoldingRange>> Foldings { get; } = new();
    public Dictionary<string, IReadOnlyDictionary<string, string?>> DeclaredTypes { get; } = new();

    private static readonly IReadOnlyDictionary<string, string?> EmptyTypes =
        new Dictionary<string, string?>();

    /// <summary>
    /// Parse the document ONCE (off-thread, under a wall-clock budget), refresh every cache, and return
    /// the diagnostics for the caller to publish. If the parse exceeds
    /// <see cref="VbDiagnosticsProvider.ParseBudget"/> the revision is treated as "analysis pending":
    /// every cache is left untouched (so the editor keeps its last-good markers rather than flickering to
    /// empty) and the last-known diagnostics are returned. The source itself is always updated first, so
    /// the text-based handlers (hover/rename/highlight) still see the current buffer.
    /// </summary>
    public async Task<List<LspDiagnostic>> UpdateDocumentAsync(string uri, string source)
    {
        Source[uri] = source;

        var result = await VbDiagnosticsProvider
            .TryGetDiagnosticsAndTreeWithin(source, VbDiagnosticsProvider.ParseBudget)
            .ConfigureAwait(false);
        if (result is null)
            return Diagnostics.TryGetValue(uri, out var prev) ? prev : []; // analysis pending — keep prior state

        var (diagnostics, tree) = result.Value;
        Diagnostics[uri]   = diagnostics;
        Symbols[uri]       = VbSymbolProvider.GetSymbols(tree);      // shares the tree (no reparse)
        Foldings[uri]      = VbFoldingProvider.GetFoldings(source);  // regex, no parse
        DeclaredTypes[uri] = tree is not null ? VbScopeAnalyzer.GetDeclaredTypes(tree) : EmptyTypes;

        return diagnostics;
    }

    public void RemoveDocument(string uri)
    {
        Source.Remove(uri);
        Diagnostics.Remove(uri);
        Symbols.Remove(uri);
        Foldings.Remove(uri);
        DeclaredTypes.Remove(uri);
    }
}
