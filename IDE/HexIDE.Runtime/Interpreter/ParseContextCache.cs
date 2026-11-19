using System.Runtime.CompilerServices;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace HexIDE.Runtime.Interpreter;

// Only syntax belongs here. Weak keys let a replaced module's tree and its cached data go together.
internal static class ParseContextCache
{
    private static readonly ConditionalWeakTable<IParseTree, Text> text = new();
    private static readonly ConditionalWeakTable<ParserRuleContext, VB6Parser.ValueStmtContext[]> values = new();

    internal static string CachedText(this IParseTree context) => text.GetValue(context, static c => new Text(c.GetText())).Value;

    internal static VB6Parser.ValueStmtContext[] CachedValues(this ParserRuleContext context) =>
        values.GetValue(context, static c => c.GetRuleContexts<VB6Parser.ValueStmtContext>());

    private sealed record Text(string Value);
}
