// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Parses VB6 source (proleap / grammars-v4 grammar) and returns LSP diagnostics.

using Antlr4.Runtime;
using Antlr4.Runtime.Atn;   // PredictionMode (two-stage SLL->LL prediction)
using Antlr4.Runtime.Misc;  // ParseCanceledException (BailErrorStrategy)

namespace HexIDE.VbLspServer;

/// <summary>Parses VB6 source and returns LSP diagnostics.</summary>
public static class VbDiagnosticsProvider
{
    // The Option-Explicit undeclared-variable check is DEFAULT-OFF in the live diagnostics path. Without a
    // workspace symbol table the analyzer cannot see form-control names, cross-module declarations, VB6
    // intrinsic functions, or vb* constants, so under Option Explicit (the professional default) it
    // squiggles a wall of false "not declared" warnings on code VB6 compiles clean — the worst signal a
    // language tool can send. VB6 itself reports undeclared variables only at COMPILE time, so live
    // squiggling is also less faithful. VbScopeAnalyzer stays fully implemented + directly tested; flip
    // this on once a workspace symbol table can resolve those names.
    public const bool EnableUndeclaredVariableCheck = false;

    public static List<LspDiagnostic> GetDiagnostics(string source)
    {
        var diagnostics = new List<LspDiagnostic>();
        var tree = ParseSource(source, diagnostics);

        // Only run scope analysis when the syntax is clean — syntax errors can produce
        // an incomplete parse tree which would cause spurious undeclared-variable warnings.
        if (EnableUndeclaredVariableCheck && diagnostics.Count == 0 && tree is not null)
            diagnostics.AddRange(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree));

        return diagnostics;
    }

    /// <summary>
    /// Parses source once, returns both the diagnostics list and the parse tree.
    /// Use this when callers also need the tree (e.g. to extract declared types).
    /// </summary>
    public static (List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree) GetDiagnosticsAndTree(string source)
    {
        var diagnostics = new List<LspDiagnostic>();
        var tree = ParseSource(source, diagnostics);

        if (EnableUndeclaredVariableCheck && diagnostics.Count == 0 && tree is not null)
            diagnostics.AddRange(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree));

        return (diagnostics, tree);
    }

    /// <summary>Wall-clock budget for a single parse. VB6's genuine call-vs-array ambiguity can push the
    /// LL stage to ~1s on a slow machine, and a rare environmental runaway (e.g. a GC stall landing on a
    /// ~120 MB parse) must never freeze the editor. 2s clears every legitimate parse with margin.</summary>
    public static readonly TimeSpan ParseBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs <see cref="GetDiagnosticsAndTree"/> on a threadpool thread with a hard wall-clock budget.
    /// Returns <c>null</c> if the budget is exceeded — the caller must treat the revision as "analysis
    /// pending" (keep previously-published results, refresh no caches). The abandoned parse runs to
    /// completion in the background (ANTLR has no mid-parse cancellation) and its result is discarded;
    /// because GetDiagnosticsAndTree is pure (touches only <paramref name="source"/>, returns fresh
    /// objects) this does not race the caches. It MUST run off-thread: inline it would block the
    /// SingleThreadScheduler's one worker for the full parse regardless of the budget.
    /// </summary>
    public static Task<(List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree)?>
        TryGetDiagnosticsAndTreeWithin(string source, TimeSpan budget)
        => RunWithin(() => GetDiagnosticsAndTree(source), budget);

    /// <summary>
    /// The budget race itself, with the work passed in.
    /// </summary>
    /// <remarks>
    /// Split out so it can be tested. Asserting this through a real parse cannot be made reliable: ANTLR
    /// caches its DFA across parses, so the same input that takes many milliseconds cold takes well under
    /// one warm — the outcome then depends on which tests ran before, and the test passes alone and fails
    /// in a full-suite run. Both attempts at a wall-clock assertion here were flaky for that reason,
    /// the second one written while fixing the first.
    ///
    /// A test supplies work whose duration it controls, and asserts the DECISION rather than the parser's
    /// speed. That is the only part of this worth asserting — the parser being fast is not a defect.
    /// </remarks>
    internal static async Task<(List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree)?>
        RunWithin(
            Func<(List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree)> work,
            TimeSpan budget)
    {
        // A budget that has already expired can admit nothing, so say so before starting work rather than
        // starting a parse and racing it against a delay that is complete before it begins.
        //
        // Without this, Task.WhenAny decides it — and WhenAny returns the first task it finds completed,
        // scanning in ARGUMENT order. With both complete it picks the parse, so a zero budget admitted a
        // fast parse whenever the thread pool happened to beat the await continuation. That is scheduling
        // luck rather than a decision, and it is what made the zero-budget test intermittent.
        if (budget <= TimeSpan.Zero)
            return null;

        var parseTask = Task.Run(work, CancellationToken.None);

        using var delayCts = new CancellationTokenSource();
        var delayTask = Task.Delay(budget, delayCts.Token);

        if (await Task.WhenAny(parseTask, delayTask).ConfigureAwait(false) == parseTask)
        {
            delayCts.Cancel(); // stop the timer so it doesn't leak
            return await parseTask.ConfigureAwait(false);
        }

        // Budget blown: abandon the parse (do NOT await it — the handler completes now, freeing the
        // worker within the budget). Observe the orphan's eventual exception so it can't surface as an
        // UnobservedTaskException.
        _ = parseTask.ContinueWith(static t => { _ = t.Exception; },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return null;
    }

    /// <summary>Keystroke-time input ceiling (~10k lines of VB6); larger inputs skip live analysis.</summary>
    private const int MaxParseInputChars = 400_000;

    /// <summary>Max parse-tree recursion depth before a degenerate deeply-nested input is aborted (see
    /// <see cref="ParseDepthGuard"/>). Real code peaks near ~50; ~600 overflows the stack — 300 sits safely between.</summary>
    internal const int MaxParseDepth = 300;

    /// <summary>
    /// Parses <paramref name="source"/> and returns the parse tree, collecting any lexer/parser errors
    /// into <paramref name="diagnostics"/> if provided. Returns null only when the input exceeds the
    /// size guard (live analysis paused).
    /// </summary>
    /// <remarks>
    /// Two-stage prediction (ANTLR's official performance recommendation): attempt SLL first with a
    /// <see cref="BailErrorStrategy"/> — far cheaper than adaptive LL(*) on the unambiguous majority, and
    /// it aborts instead of running LL-costly error recovery. Only on failure do we rewind and re-parse
    /// with full LL + the collecting listener, which is authoritative. Correctness is preserved by the
    /// Adaptive LL(*) theorem: an SLL success yields the same tree LL would, and SLL only *errors* where
    /// LL might have succeeded — exactly when we fall back. (SLL-only was rejected empirically: it
    /// mispredicts the call/array/member decision, flagging valid VB6 like <c>x = Foo(1)</c> as errors.)
    /// </remarks>
    internal static VisualBasic6Parser.StartRuleContext? ParseSource(string source, List<LspDiagnostic>? diagnostics = null)
    {
        // Defense-in-depth: never let a giant paste drive a multi-second parse on the keystroke path.
        if (source.Length > MaxParseInputChars)
        {
            diagnostics?.Add(new LspDiagnostic(
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                "File too large for live analysis; diagnostics paused.",
                2));
            return null;
        }

        var inputStream = new AntlrInputStream(source);
        var lexer = new VisualBasic6Lexer(inputStream);
        lexer.RemoveErrorListeners();
        DiagnosticErrorListener? errorListener = diagnostics is not null ? new DiagnosticErrorListener(diagnostics) : null;
        if (errorListener is not null)
            lexer.AddErrorListener(errorListener);

        var tokenStream = new CommonTokenStream(lexer);
        // Force full tokenisation so every lexer (token-recognition) error is reported. proleap has NO
        // ERRORCHAR token — unrecognised characters surface as lexer errors through the listener above.
        // The filled buffer is reused by the LL re-parse below (the lexer never runs twice).
        tokenStream.Fill();

        var parser = new VisualBasic6Parser(tokenStream);

        // A degenerate deeply-nested input (hundreds of nested parens/blocks) would overflow the recursive-descent
        // parser's C# stack — an UNCATCHABLE crash that kills the server regardless of thread. The depth guard aborts
        // such a parse with a catchable exception well before the overflow; we surface it as a paused-analysis
        // diagnostic. A fresh guard is used per prediction stage because an SLL bail unwinds without balancing the
        // depth counter.
        try
        {
            // ── Stage 1: SLL fast path ────────────────────────────────────────────────────────────
            // No error listeners here — SLL's speculative errors must not be reported; Bail aborts on the first
            // error instead of running the LL-costly error-recovery/resync machinery that IS the blowup.
            parser.RemoveErrorListeners();
            parser.AddParseListener(new ParseDepthGuard(MaxParseDepth));
            parser.Interpreter.PredictionMode = PredictionMode.SLL;
            parser.ErrorHandler = new BailErrorStrategy();
            try
            {
                // SLL success ⇒ tree identical to LL's (Adaptive LL(*) guarantee).
                return parser.startRule();
            }
            catch (ParseCanceledException)
            {
                // ── Stage 2: authoritative LL(*) re-parse ─────────────────────────────────────────
                // SLL found a real error or mispredicted an ambiguous construct; LL with normal recovery +
                // the collecting listener is correct for both cases.
                parser.Reset();          // resets parser state + rewinds the input to token 0
                tokenStream.Seek(0);     // belt-and-suspenders (no re-lex — buffer already filled)
                parser.RemoveParseListeners();                          // drop the SLL guard (its counter is now dirty)
                parser.AddParseListener(new ParseDepthGuard(MaxParseDepth));
                parser.ErrorHandler = new DefaultErrorStrategy();
                parser.Interpreter.PredictionMode = PredictionMode.LL;
                if (errorListener is not null)
                    parser.AddErrorListener(errorListener);
                return parser.startRule();
            }
        }
        catch (ParseNestingTooDeepException)
        {
            diagnostics?.Add(new LspDiagnostic(
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                "Expression or block nesting too deep for analysis; diagnostics paused.",
                2));
            return null;
        }
    }

    private sealed class DiagnosticErrorListener(List<LspDiagnostic> diagnostics)
        : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            var tokenText = offendingSymbol?.Text;
            var tokenLen = tokenText is { } t && t != "<EOF>" ? t.Length : 1;
            diagnostics.Add(new LspDiagnostic(
                new LspRange(
                    new LspPosition(line - 1, charPositionInLine),
                    new LspPosition(line - 1, charPositionInLine + tokenLen)),
                VbErrorMessages.Prettify(msg, tokenText),
                1));
        }

        // Lexer error overload
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            diagnostics.Add(new LspDiagnostic(
                new LspRange(
                    new LspPosition(line - 1, charPositionInLine),
                    new LspPosition(line - 1, charPositionInLine + 1)),
                VbErrorMessages.Prettify(msg, ((char)offendingSymbol).ToString()),
                1));
        }
    }
}

// ── Minimal internal LSP model (mapped to framework protocol types in Phase 4) ──

public record LspPosition(int Line, int Character);
public record LspRange(LspPosition Start, LspPosition End);
public record LspDiagnostic(LspRange Range, string Message, int Severity);

public enum LspCompletionItemKind
{
    Text     = 1,
    Function = 3,
    Variable = 6,
    Property = 10,
    Keyword  = 14,
    Constant = 21,
}

public record LspCompletionItem(
    string Label,
    LspCompletionItemKind Kind,
    string? Detail = null,
    string? InsertText = null);
