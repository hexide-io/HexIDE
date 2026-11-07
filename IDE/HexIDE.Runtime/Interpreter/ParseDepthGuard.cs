using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace HexIDE.Runtime.Interpreter;

/// <summary>Refuses excessive parser recursion before it can overflow the host stack (#3210).
/// Sized workers derive their rule count from the measured stack cost; other callers retain the old bound.
/// The physical probe also covers heavier prediction/recovery paths between rule entries.</summary>
internal sealed class ParseDepthGuard : IParseTreeListener
{
    // Still used by the conditional-expression finished-tree check; lifted separately.
    internal const int DefaultMaxDepth = 300;

    private readonly int _maxDepth;
    private readonly string? _moduleName;
    private int _depth;

    /// <param name="moduleName">The module being parsed, named in the error as a syntax error names it, so the person
    /// fixes the right file. Null where there is no module, as for an Immediate-window entry.</param>
    public ParseDepthGuard(int? maxDepth = null, string? moduleName = null)
    {
        _maxDepth = maxDepth ?? ParseStack.RuleDepth;
        _moduleName = moduleName;
    }

    // Prediction can inspect the whole parenthesized expression before entering its inner rules.
    // Bound that shape before prediction allocates its configurations, using #3210's ~3.6 KB/pair.
    internal static void CheckNesting(VB6Parser parser, string? moduleName = null)
    {
        const int maxPairs = ParseStack.StackSize / (2 * 3600);
        var tokens = (BufferedTokenStream)parser.TokenStream;
        tokens.Fill();
        var depth = 0;
        foreach (var symbol in tokens.GetTokens())
        {
            if (symbol.Type == VB6Lexer.LPAREN && ++depth > maxPairs)
                throw new VBCompileErrorException($"Expression or block nesting too deep (exceeds {maxPairs} parenthesis pairs)"
                    + (moduleName is null ? "" : $" in {moduleName}, line {symbol.Line}")) { Line = symbol.Line };
            if (symbol.Type == VB6Lexer.RPAREN)
                depth = Math.Max(0, depth - 1);
        }
        // A reverse balance is a lower bound: inline If tokens can only reduce it. It is not a
        // statement parser, but stops an oversized tail of End Ifs before prediction scans it.
        const int maxBlocks = ParseStack.StackSize / (3 * 2800);
        depth = 0;
        for (var i = tokens.Size - 1; i >= 0; i--)
        {
            var symbol = tokens.Get(i);
            if (symbol.Type == VB6Lexer.END_IF && ++depth > maxBlocks)
                throw new VBCompileErrorException($"Expression or block nesting too deep (exceeds {maxBlocks} nested If blocks)"
                    + (moduleName is null ? "" : $" in {moduleName}, line {symbol.Line}")) { Line = symbol.Line };
            if (symbol.Type == VB6Lexer.IF)
                depth = Math.Max(0, depth - 1);
        }
    }

    public void EnterEveryRule(ParserRuleContext ctx)
    {
        var tooDeep = ++_depth > _maxDepth;
        if (tooDeep || !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            var line = ctx.Start?.Line ?? 0;
            var reason = tooDeep ? $"exceeds {_maxDepth} rule levels" : "insufficient parser stack space";
            throw new VBCompileErrorException($"Expression or block nesting too deep ({reason})"
                + (_moduleName is null ? "" : $" in {_moduleName}, line {line}")) { Line = line };
        }
    }

    public void ExitEveryRule(ParserRuleContext ctx) => _depth--;
    public void VisitTerminal(ITerminalNode node) { }
    public void VisitErrorNode(IErrorNode node) { }
}
