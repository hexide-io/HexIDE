using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Bounds parse-tree recursion depth so a degenerate deeply-nested input (hundreds of nested parentheses or blocks)
/// is rejected as a compile error instead of overflowing the C# stack — a <see cref="System.StackOverflowException"/>
/// is UNCATCHABLE and takes down the whole process. The proleap VB6 grammar is recursive-descent, so parse-tree depth
/// ≈ C# stack depth; this listener fires on every rule entry (before the parser descends further) and aborts once
/// depth exceeds the limit — proven to fire well before the ~600-level overflow, under both SLL and LL prediction.
/// </summary>
/// <remarks>
/// The limit is a wide margin above real code (a deliberately deep real procedure peaks near a rule-depth of ~50,
/// and each nesting level adds ≈1 rule frame). <b>Divergence:</b> vb6.exe itself tolerates far deeper nesting
/// (verified: it compiles 4096 nested parens), so this rejects absurd nesting that VB6 would accept — a clean
/// "nesting too deep" compile error instead of matching an effectively-unbounded stack. See docs/interpreter-gaps.md.
/// </remarks>
internal sealed class ParseDepthGuard : IParseTreeListener
{
    internal const int DefaultMaxDepth = 300;

    private readonly int _maxDepth;
    private int _depth;

    public ParseDepthGuard(int maxDepth = DefaultMaxDepth) => _maxDepth = maxDepth;

    public void EnterEveryRule(ParserRuleContext ctx)
    {
        if (++_depth > _maxDepth)
            throw new VBCompileErrorException(
                $"Expression or block nesting too deep (exceeds {_maxDepth} levels)") { Line = ctx.Start?.Line ?? 0 };
    }

    public void ExitEveryRule(ParserRuleContext ctx) => _depth--;
    public void VisitTerminal(ITerminalNode node) { }
    public void VisitErrorNode(IErrorNode node) { }
}
