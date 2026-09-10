// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// What one parse cost and which stage answered it — the facts a wire capture cannot recover.

namespace HexIDE.VbLspServer;

/// <summary>
/// Which prediction stage produced the tree. See <see cref="VbDiagnosticsProvider.ParseSource"/> for why
/// there are two of them; the distinction is invisible from outside the process, and it is the single
/// biggest determinant of how long an analysis took.
/// </summary>
public enum ParsePrediction
{
    /// <summary>No parse ran, or none completed — the input was refused or abandoned before a tree existed.</summary>
    None,

    /// <summary>The SLL fast path answered. Its tree is the one LL would have produced.</summary>
    Sll,

    /// <summary>SLL bailed; the authoritative LL(*) re-parse produced the tree. The expensive case.</summary>
    Ll,
}

/// <summary>How an analysis ended.</summary>
public enum ParseOutcome
{
    /// <summary>A tree was produced (with or without diagnostics against it).</summary>
    Parsed,

    /// <summary>Over <c>MaxParseInputChars</c>; live analysis paused without parsing.</summary>
    InputTooLarge,

    /// <summary>The depth guard fired; the parse was abandoned rather than overflowing the stack.</summary>
    NestingTooDeep,

    /// <summary>
    /// The wall-clock budget expired first. The parse is still running, orphaned, and the caller kept
    /// whatever it had published before. Never set by the parse itself — only by the caller that raced it.
    /// </summary>
    BudgetExhausted,
}

/// <summary>
/// A per-parse report, allocated only when someone asked for one.
/// </summary>
/// <remarks>
/// <para>
/// One instance per parse, never shared and never reused. That is load-bearing rather than tidy: a parse
/// that outlives its wall-clock budget is abandoned and runs to completion in the background, so a report
/// object visible to the caller would be written by the orphan after the caller had already read it. A
/// fresh instance returned <em>with</em> the result means the orphan's report goes where its result goes —
/// nowhere.
/// </para>
/// <para>
/// The setters are internal: this is a record of what happened, not a knob.
/// </para>
/// </remarks>
public sealed class ParseReport
{
    /// <summary>Which prediction stage produced the tree.</summary>
    public ParsePrediction Prediction { get; internal set; } = ParsePrediction.None;

    /// <summary>How the parse ended.</summary>
    public ParseOutcome Outcome { get; internal set; } = ParseOutcome.Parsed;

    /// <summary>Wall-clock time spent in the parse itself, excluding the hop onto the thread pool.</summary>
    public TimeSpan Elapsed { get; internal set; }
}
