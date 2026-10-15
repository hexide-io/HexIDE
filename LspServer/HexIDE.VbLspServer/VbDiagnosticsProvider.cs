// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Parses VB6 source (proleap / grammars-v4 grammar) and returns LSP diagnostics.

using System.Diagnostics;   // Stopwatch (the reported parse only)
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
        var regions = VbProtectedRegions.Of(source);
        var diagnostics = new List<LspDiagnostic>();
        var tree = ParseSource(source, diagnostics, protectedRegions: regions);

        // Only run scope analysis when the syntax is clean — syntax errors can produce
        // an incomplete parse tree which would cause spurious undeclared-variable warnings.
        if (EnableUndeclaredVariableCheck && diagnostics.Count == 0 && tree is not null)
            diagnostics.AddRange(OutsideProtectedLines(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree), regions));

        return diagnostics;
    }

    /// <summary>
    /// Parses source once, returns both the diagnostics list and the parse tree.
    /// Use this when callers also need the tree (e.g. to extract declared types).
    /// </summary>
    public static (List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree) GetDiagnosticsAndTree(string source)
    {
        var regions = VbProtectedRegions.Of(source);
        var diagnostics = new List<LspDiagnostic>();
        var tree = ParseSource(source, diagnostics, protectedRegions: regions);

        if (EnableUndeclaredVariableCheck && diagnostics.Count == 0 && tree is not null)
            diagnostics.AddRange(OutsideProtectedLines(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree), regions));

        return (diagnostics, tree);
    }

    /// <summary>
    /// The parse's diagnostics with nothing held back, including those inside a header or a member's
    /// attribute lines, which every other entry point here leaves out.
    /// </summary>
    /// <remarks>
    /// For the grammar's own whole-file check (<c>WholeFileGrammarTests</c>), which exists to find a header
    /// the grammar cannot parse. Given the filtered answer it would pass whatever the grammar did with a
    /// header, because it would never be shown an error there. Not a path the server answers from.
    /// </remarks>
    internal static (List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree)
        GetDiagnosticsAndTreeIncludingProtectedLines(string source)
    {
        var diagnostics = new List<LspDiagnostic>();
        var tree = ParseSource(source, diagnostics);
        return (diagnostics, tree);
    }

    /// <summary>
    /// The same parse as <see cref="GetDiagnosticsAndTree"/>, plus a <see cref="ParseReport"/> saying which
    /// prediction stage answered and what it cost.
    /// </summary>
    /// <remarks>
    /// A separate entry point rather than an extra out-parameter on the existing one, for two reasons.
    /// Existing callers keep byte-for-byte what they get today. And nothing is measured unless someone
    /// asked: a client that never turns tracing on never allocates a report and never reads a timestamp.
    /// The parse itself is identical either way — the report is written from inside it, and reading the
    /// stage cannot change it.
    /// </remarks>
    public static (List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree, ParseReport Report)
        GetDiagnosticsAndTreeReported(string source)
    {
        var report = new ParseReport();
        var diagnostics = new List<LspDiagnostic>();

        var startedAt = Stopwatch.GetTimestamp();
        var regions = VbProtectedRegions.Of(source);
        var tree = ParseSource(source, diagnostics, report, regions);
        report.Elapsed = Stopwatch.GetElapsedTime(startedAt);

        if (EnableUndeclaredVariableCheck && diagnostics.Count == 0 && tree is not null)
            diagnostics.AddRange(OutsideProtectedLines(VbScopeAnalyzer.GetOptionExplicitDiagnostics(tree), regions));

        return (diagnostics, tree, report);
    }

    /// <summary>
    /// <paramref name="diagnostics"/> less any that start on a line of the header or of a member's attribute
    /// run (hexide-io/HexIDE#273 task 3.10).
    /// </summary>
    private static IEnumerable<LspDiagnostic> OutsideProtectedLines(
        IEnumerable<LspDiagnostic> diagnostics, VbProtectedRegions regions) =>
        diagnostics.Where(d => !regions.IsProtected(d.Range.Start.Line));

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
    /// <see cref="TryGetDiagnosticsAndTreeWithin"/> with a <see cref="ParseReport"/> attached. Identical
    /// race, identical abandonment: a <c>null</c> return still means "analysis pending", and the report of
    /// the abandoned parse is discarded along with its result rather than being handed back half-written.
    /// </summary>
    public static Task<(List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree, ParseReport Report)?>
        TryGetDiagnosticsAndTreeReportedWithin(string source, TimeSpan budget)
        => RunWithinCore(() => GetDiagnosticsAndTreeReported(source), budget);

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
    internal static Task<(List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree)?>
        RunWithin(
            Func<(List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree)> work,
            TimeSpan budget)
        => RunWithinCore(work, budget);

    /// <summary>
    /// The race itself, over whatever the work returns. Kept generic so the reported and unreported parses
    /// share one implementation — the abandonment rules below are the subtle part, and having two copies of
    /// them is how they drift apart. <see cref="RunWithin"/> stays non-generic so the tuple element names
    /// its tests read survive type inference.
    /// </summary>
    private static async Task<T?> RunWithinCore<T>(Func<T> work, TimeSpan budget) where T : struct
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
    /// <param name="report">
    /// Optional, and null on every path that has not asked for tracing. Nothing here reads it, branches on
    /// it, or feeds it back into the parse — it is written at the three points where the outcome becomes
    /// known and nowhere else, so the parse behaves identically whether or not one was passed.
    /// </param>
    /// <param name="protectedRegions">
    /// The lines on which no parse error is reported: the header and members' attribute lines. Null reports
    /// every error, which only the grammar's own whole-file check asks for. The notices this method raises
    /// itself, for a file too large to analyse or nested too deep, are about the file rather than a line,
    /// and are raised whatever this holds, although they sit at (0,0).
    /// </param>
    /// <remarks>
    /// <b>When the grammar cannot read a protected line, the file is parsed again with those lines
    /// emptied</b>, and that parse is the answer. Leaving the error out is not enough on its own: ANTLR's
    /// recovery from a damaged designer block can consume the code after it, so the first parse reports
    /// nothing past the header at all -- measured, with a syntax error in the code that the first parse never
    /// reported. Leaving out the header's errors then left a developer with no diagnostics and no reason why.
    /// Emptying a line keeps its line break, so every line of code keeps the line and column it had, and a
    /// header the grammar does read is still in the tree. Only a file with such an error pays for a second
    /// parse, and no file in the corpus has one.
    /// </remarks>
    internal static VisualBasic6Parser.StartRuleContext? ParseSource(
        string source, List<LspDiagnostic>? diagnostics = null, ParseReport? report = null,
        VbProtectedRegions? protectedRegions = null)
    {
        var reportedBefore = diagnostics?.Count ?? 0;
        var tree = ParseOnce(source, diagnostics, report, protectedRegions, out var leftOutAnError);
        if (!leftOutAnError || protectedRegions is null)
            return tree;

        diagnostics?.RemoveRange(reportedBefore, diagnostics.Count - reportedBefore);
        return ParseOnce(protectedRegions.Blank(source), diagnostics, report, protectedRegions, out _);
    }

    private static VisualBasic6Parser.StartRuleContext? ParseOnce(
        string source, List<LspDiagnostic>? diagnostics, ParseReport? report,
        VbProtectedRegions? protectedRegions, out bool leftOutAnError)
    {
        leftOutAnError = false;
        // Defense-in-depth: never let a giant paste drive a multi-second parse on the keystroke path.
        if (source.Length > MaxParseInputChars)
        {
            diagnostics?.Add(new LspDiagnostic(
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                "File too large for live analysis; diagnostics paused.",
                2));
            if (report is not null) report.Outcome = ParseOutcome.InputTooLarge;
            return null;
        }

        var inputStream = new AntlrInputStream(source);
        var lexer = new VisualBasic6Lexer(inputStream);
        lexer.RemoveErrorListeners();
        DiagnosticErrorListener? errorListener = diagnostics is not null ? new DiagnosticErrorListener(diagnostics, protectedRegions) : null;
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
                var tree = parser.startRule();
                // After the call, not before: a bail or a depth abort must not be recorded as an SLL answer.
                if (report is not null) report.Prediction = ParsePrediction.Sll;
                // The lexer's errors still count here, though SLL itself reports none.
                leftOutAnError = errorListener?.LeftOutAny == true;
                return tree;
            }
            catch (ParseCanceledException)
            {
                // ── Stage 2: authoritative LL(*) re-parse ─────────────────────────────────────────
                // SLL found a real error or mispredicted an ambiguous construct; LL with normal recovery +
                // the collecting listener is correct for both cases.
                // Before the re-parse, not after — unlike the SLL arm. Reaching this branch at all is the
                // fact worth reporting: it is why the analysis cost what it did, whether or not LL then
                // finishes.
                if (report is not null) report.Prediction = ParsePrediction.Ll;
                parser.Reset();          // resets parser state + rewinds the input to token 0
                tokenStream.Seek(0);     // belt-and-suspenders (no re-lex — buffer already filled)
                parser.RemoveParseListeners();                          // drop the SLL guard (its counter is now dirty)
                parser.AddParseListener(new ParseDepthGuard(MaxParseDepth));
                parser.ErrorHandler = new DefaultErrorStrategy();
                parser.Interpreter.PredictionMode = PredictionMode.LL;
                if (errorListener is not null)
                    parser.AddErrorListener(errorListener);
                var tree = parser.startRule();
                leftOutAnError = errorListener?.LeftOutAny == true;
                return tree;
            }
        }
        catch (ParseNestingTooDeepException)
        {
            diagnostics?.Add(new LspDiagnostic(
                new LspRange(new LspPosition(0, 0), new LspPosition(0, 1)),
                "Expression or block nesting too deep for analysis; diagnostics paused.",
                2));
            if (report is not null) report.Outcome = ParseOutcome.NestingTooDeep;
            return null;
        }
    }

    /// <summary>
    /// Turns ANTLR's errors into diagnostics, leaving out any that start on a protected line.
    /// </summary>
    /// <remarks>
    /// The server raises no diagnostic inside a header or a member's attribute lines (hexide-io/HexIDE#273
    /// task 3.10). The grammar parses every header in the corpus cleanly, so on a real file there is nothing
    /// to leave out; this is what keeps a damaged one from burying the code's real diagnostics under false
    /// ones about lines the developer cannot edit. Leaving one out is also what sends
    /// <see cref="ParseSource"/> round for its second parse.
    /// </remarks>
    private sealed class DiagnosticErrorListener(List<LspDiagnostic> diagnostics, VbProtectedRegions? protectedRegions)
        : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
    {
        /// <summary>True once an error on a protected line has been left out.</summary>
        public bool LeftOutAny { get; private set; }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol,
            int line, int charPositionInLine, string msg, RecognitionException e)
        {
            if (protectedRegions?.IsProtected(line - 1) == true)
            {
                LeftOutAny = true;
                return;
            }
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
            if (protectedRegions?.IsProtected(line - 1) == true)
            {
                LeftOutAny = true;
                return;
            }
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
