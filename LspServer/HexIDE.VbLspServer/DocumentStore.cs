// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Per-URI document cache for the LSP server. Written only from the sequential notification handlers;
// the parse runs off-thread (see UpdateDocumentAsync) but the SingleThreadScheduler awaits each handler
// to completion, so cache access is never concurrent — no locking required.

using System.Diagnostics;

namespace HexIDE.VbLspServer;

/// <summary>
/// What one analysis of one document did, for the server's own trace channel. Every field here is
/// something a client watching the wire cannot recover: it sees the diagnostics that came out, never
/// which prediction stage produced them, what that cost, or whether anything was produced at all.
/// </summary>
internal sealed record DocumentAnalysis(
    ParseOutcome Outcome,
    ParsePrediction Prediction,
    TimeSpan Elapsed,
    int SourceLength,
    int DiagnosticCount,
    int SymbolCount);

/// <summary>
/// The result of a document update: always the diagnostics to publish, plus — only when a trace was
/// asked for — what the analysis did to produce them.
/// </summary>
internal readonly record struct DocumentUpdate(List<LspDiagnostic> Diagnostics, DocumentAnalysis? Analysis);

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
    /// <param name="collectAnalysis">
    /// Whether to also report what the analysis did. False is the whole-of-history path and stays exactly
    /// as it was: no report allocated, no clock read, the same provider call as before. A developer who
    /// never turns tracing on pays nothing for its existence.
    /// </param>
    public async Task<DocumentUpdate> UpdateDocumentAsync(string uri, string source, bool collectAnalysis = false)
    {
        Source[uri] = source;

        if (!collectAnalysis)
        {
            var plain = await VbDiagnosticsProvider
                .TryGetDiagnosticsAndTreeWithin(source, VbDiagnosticsProvider.ParseBudget)
                .ConfigureAwait(false);
            if (plain is null)
                return new DocumentUpdate(PriorDiagnostics(uri), null); // analysis pending — keep prior state

            var (plainDiagnostics, plainTree) = plain.Value;
            Refresh(uri, source, plainDiagnostics, plainTree);
            return new DocumentUpdate(plainDiagnostics, null);
        }

        // The outer clock, which the report cannot supply: when the budget wins the race there is no
        // report at all, and "how long before we gave up" is the only number worth having.
        var startedAt = Stopwatch.GetTimestamp();
        var traced = await VbDiagnosticsProvider
            .TryGetDiagnosticsAndTreeReportedWithin(source, VbDiagnosticsProvider.ParseBudget)
            .ConfigureAwait(false);

        if (traced is null)
        {
            var prior = PriorDiagnostics(uri);
            return new DocumentUpdate(prior, new DocumentAnalysis(
                ParseOutcome.BudgetExhausted,
                ParsePrediction.None, // the orphan may still settle on a stage; we are not there to hear it
                Stopwatch.GetElapsedTime(startedAt),
                source.Length,
                prior.Count,
                PriorSymbolCount(uri)));
        }

        var (diagnostics, tree, report) = traced.Value;
        Refresh(uri, source, diagnostics, tree);
        return new DocumentUpdate(diagnostics, new DocumentAnalysis(
            report.Outcome,
            report.Prediction,
            report.Elapsed,
            source.Length,
            diagnostics.Count,
            Symbols[uri].Count));
    }

    public void RemoveDocument(string uri)
    {
        Source.Remove(uri);
        Diagnostics.Remove(uri);
        Symbols.Remove(uri);
        Foldings.Remove(uri);
        DeclaredTypes.Remove(uri);
    }

    private void Refresh(string uri, string source, List<LspDiagnostic> diagnostics,
                         VisualBasic6Parser.StartRuleContext? tree)
    {
        Diagnostics[uri]   = diagnostics;
        Symbols[uri]       = VbSymbolProvider.GetSymbols(tree);      // shares the tree (no reparse)
        Foldings[uri]      = VbFoldingProvider.GetFoldings(source);  // regex, no parse
        DeclaredTypes[uri] = tree is not null ? VbScopeAnalyzer.GetDeclaredTypes(tree) : EmptyTypes;
    }

    private List<LspDiagnostic> PriorDiagnostics(string uri) =>
        Diagnostics.TryGetValue(uri, out var prev) ? prev : [];

    private int PriorSymbolCount(string uri) =>
        Symbols.TryGetValue(uri, out var prev) ? prev.Count : 0;
}
