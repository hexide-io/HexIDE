// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace HexIDE.VbLspServer;

/// <summary>Thrown by <see cref="ParseDepthGuard"/> when parse-tree recursion exceeds the depth limit.</summary>
public sealed class ParseNestingTooDeepException(int maxDepth)
    : Exception($"Parse nesting exceeded {maxDepth} levels");

/// <summary>
/// Bounds parse-tree recursion depth so a degenerate deeply-nested input (hundreds of nested parentheses or blocks)
/// is aborted instead of overflowing the C# stack — a <see cref="StackOverflowException"/> is UNCATCHABLE and would
/// terminate the whole LSP server process regardless of thread (so the wall-clock backstop can't save it). The
/// proleap grammar is recursive-descent, so parse-tree depth ≈ C# stack depth; this listener fires on every rule
/// entry (before the parser descends further) and aborts once depth exceeds the limit — proven to fire well before
/// the ~600-level overflow, under both SLL and LL prediction.
/// </summary>
/// <remarks>
/// The limit is a wide margin above real code (a deliberately deep real procedure peaks near a rule-depth of ~50,
/// each nesting level adding ≈1 rule frame). This deliberately rejects absurd nesting that vb6.exe would accept
/// (it compiles 4096 nested parens) — a paused-analysis diagnostic instead of a process crash.
/// </remarks>
internal sealed class ParseDepthGuard : IParseTreeListener
{
    private readonly int _maxDepth;
    private int _depth;

    public ParseDepthGuard(int maxDepth) => _maxDepth = maxDepth;

    public void EnterEveryRule(ParserRuleContext ctx)
    {
        if (++_depth > _maxDepth)
            throw new ParseNestingTooDeepException(_maxDepth);
    }

    public void ExitEveryRule(ParserRuleContext ctx) => _depth--;
    public void VisitTerminal(ITerminalNode node) { }
    public void VisitErrorNode(IErrorNode node) { }
}
