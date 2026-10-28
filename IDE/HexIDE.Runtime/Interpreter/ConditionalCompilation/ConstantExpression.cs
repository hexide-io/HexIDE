using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>
/// The expression of an <c>#If</c>, an <c>#ElseIf</c> or a <c>#Const</c>, read by the interpreter's own parser and evaluated
/// as vb6.exe evaluates one (#1112). Measured with <c>/make</c>, native and p-code alike (the oracle's "Conditional
/// compilation"):
/// <list type="bullet">
/// <item>The operators are the language's, so this applies the interpreter's own: <c>-2 ^ 2</c> is -4, <c>Not</c> is bitwise,
/// and <c>7 \ 2 * 2</c> is 1. A typed overflow, as in <c>32767 + 1</c>, is "Overflow", and <c>1 / 0</c> "Division by zero".</item>
/// <item>A constant is a Variant and a literal is typed, as in running code, so <c>#Const M = 32767</c> then <c>M + 1</c> widens.</item>
/// <item>Strings compare as text, whatever <c>Option Compare</c> says: <c>"a" = "A"</c>, and <c>"Z" &lt; "a"</c> is false.
/// The order is vb6.exe's (VbTextCompare, #1598), so <c>"_" &lt; "a"</c> and <c>"co-op" &gt; "coop"</c> hold. <c>Like</c>
/// matches as text too, in either kind of module: <c>"a" Like "A"</c> and <c>"M" Like "[a-z]"</c> hold, and
/// <c>"_" Like "[A-z]"</c> does not. A pattern it cannot read is "Invalid pattern string".</item>
/// <item>A String against a number depends on which side is the Variant; see <see cref="Order"/>.</item>
/// <item>Only <c>Int</c>, <c>Fix</c>, <c>Abs</c>, <c>Sgn</c>, <c>Len</c>, <c>LenB</c>, <c>CBool</c>, <c>CByte</c>, <c>CCur</c>,
/// <c>CDate</c>, <c>CDbl</c>, <c>CInt</c>, <c>CLng</c>, <c>CSng</c>, <c>CStr</c> and <c>CVar</c> may be called. Any other name
/// called, <c>CDec</c> and a user's Function among them, is "Variable not defined". <c>CVErr</c> is accepted too.</item>
/// </list>
/// Every error here is a compile error, raised as the module loads.
/// </summary>
internal static class ConstantExpression
{
    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Int", "Fix", "Abs", "Sgn", "Len", "LenB", "CBool", "CByte", "CCur", "CDate", "CDbl", "CInt", "CLng", "CSng", "CStr", "CVar",
        "CVErr",   // accepted by vb6.exe, where CVErr(5) = 5 is Type mismatch
    };

    /// <summary>
    /// The expression <paramref name="text"/> holds, or null where it holds none: a line whose expression does not parse is
    /// not a directive to vb6.exe, which compiles it as code, or skips it in an inactive branch (measured, <c>#If (( Then</c>).
    /// </summary>
    internal static VB6Parser.ValueStmtContext? TryParse(string text)
    {
        if (text.Length == 0)
            return null;
        var lexer = new VB6Lexer(new AntlrInputStream(text));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(Refusal.Instance);
        var parser = new VB6Parser(new CommonTokenStream(lexer)) { ErrorHandler = new BailErrorStrategy() };
        parser.RemoveErrorListeners();
        parser.AddParseListener(new ParseDepthGuard());
        try
        {
            var expression = parser.valueStmt();
            if (parser.CurrentToken.Type != TokenConstants.EOF)
                return null;
            // A flat chain passes the parser's rule-depth guard but builds a deep, left-associated tree.
            var pending = new Stack<(IParseTree Node, int Depth)>();
            pending.Push((expression, 1));
            while (pending.TryPop(out var item))
            {
                if (item.Depth > ParseDepthGuard.DefaultMaxDepth)
                    return null;
                for (var i = 0; i < item.Node.ChildCount; i++)
                    pending.Push((item.Node.GetChild(i), item.Depth + 1));
            }
            return expression;
        }
        catch (Exception)   // the bail, the lexer's refusal, or the depth guard: not an expression
        {
            return null;
        }
    }

    /// <summary>
    /// The expression's value. <paramref name="lookup"/> gives a name's value, which is never missing: a name nothing defines
    /// is a Variant holding 0.
    /// </summary>
    internal static Vb6Value Evaluate(VB6Parser.ValueStmtContext expression, Func<string, Vb6Value> lookup,
        bool callsReadZero = false)
    {
        try
        {
            return Value(expression, lookup, callsReadZero);
        }
        catch (VBRunTimeException e)
        {
            throw new VBCompileErrorException(e.Error.Description);
        }
    }

    /// <summary>
    /// Whether a condition holds, read as <c>CBool</c> reads it: <c>"1"</c>, <c>"True"</c> and a date hold, and <c>""</c> and
    /// <c>"abc"</c> are "Type mismatch". <c>Null</c> is "Invalid use of Null" and <c>Nothing</c> "Invalid use of object" (measured).
    /// </summary>
    internal static bool Holds(Vb6Value condition)
    {
        if (condition.IsNull)
            throw new VBCompileErrorException("Invalid use of Null");
        if (condition.Type == Vb6Value.ValueType.Object)
            throw new VBCompileErrorException("Invalid use of object");
        try
        {
            return (bool)VB6BuiltIns.CBool(condition).Value!;
        }
        catch (VBRunTimeException e)
        {
            throw new VBCompileErrorException(e.Error.Description);
        }
    }

    private static Vb6Value Value(VB6Parser.ValueStmtContext node, Func<string, Vb6Value> lookup, bool callsReadZero)
    {
        Vb6Value Of(VB6Parser.ValueStmtContext operand) => Value(operand, lookup, callsReadZero);
        return node switch
        {
            VB6Parser.VsLiteralContext literal => ExpressionExecutor.LiteralValue(literal.literal()).AsFixedType(),
            VB6Parser.VsStructContext group when group.valueStmt().Length == 1 => Of(group.valueStmt(0)),
            VB6Parser.VsNegationContext negation => ExpressionExecutor.Negation(Of(negation.valueStmt()), negation),
            VB6Parser.VsNotContext not => ExpressionExecutor.Not(Of(not.valueStmt()), not),
            VB6Parser.VsPowContext pow => ExpressionExecutor.Power(Of(pow.valueStmt(0)), Of(pow.valueStmt(1)), pow),
            VB6Parser.VsMultContext mult =>
                ExpressionExecutor.MultiplyOrDivide(Of(mult.valueStmt(0)), Of(mult.valueStmt(1)), mult.DIV() != null, mult),
            VB6Parser.VsDivContext div => ExpressionExecutor.IntegerDivide(Of(div.valueStmt(0)), Of(div.valueStmt(1)), div),
            VB6Parser.VsModContext mod => ExpressionExecutor.Modulo(Of(mod.valueStmt(0)), Of(mod.valueStmt(1)), mod),
            VB6Parser.VsAddContext add =>
                ExpressionExecutor.AddOrSubtract(Of(add.valueStmt(0)), Of(add.valueStmt(1)), add.MINUS() != null, add),
            VB6Parser.VsAmpContext amp => ExpressionExecutor.Concatenate(Of(amp.valueStmt(0)), Of(amp.valueStmt(1))),
            VB6Parser.VsCompareContext compare => Compare(compare, Of(compare.valueStmt(0)), Of(compare.valueStmt(1))),
            VB6Parser.VsAndContext and => ExpressionExecutor.And(Of(and.valueStmt(0)), Of(and.valueStmt(1)), and),
            VB6Parser.VsOrContext or => ExpressionExecutor.Or(Of(or.valueStmt(0)), Of(or.valueStmt(1)), or),
            VB6Parser.VsXorContext xor => ExpressionExecutor.Xor(Of(xor.valueStmt(0)), Of(xor.valueStmt(1)), xor),
            VB6Parser.VsEqvContext eqv => ExpressionExecutor.Eqv(Of(eqv.valueStmt(0)), Of(eqv.valueStmt(1)), eqv),
            VB6Parser.VsImpContext imp => ExpressionExecutor.Imp(Of(imp.valueStmt(0)), Of(imp.valueStmt(1)), imp),
            VB6Parser.VsICSContext name => NameOrCall(name.implicitCallStmt_InStmt(), lookup, callsReadZero),
            _ => throw Unsupported(node),
        };
    }

    private static Vb6Value NameOrCall(VB6Parser.ImplicitCallStmt_InStmtContext call, Func<string, Vb6Value> lookup,
        bool callsReadZero)
    {
        if (call.iCS_S_VariableOrProcedureCall() is { } bare && bare.typeHint() is null && bare.dictionaryCallStmt() is null)
            return lookup(bare.ambiguousIdentifier().GetText());
        if (call.iCS_S_ProcedureOrArrayCall() is { } called && called.typeHint() is null && called.dictionaryCallStmt() is null
            && called.iCS_S_NestedProcedureCall() is null && called.argsCall().Length <= 1)
        {
            var name = called.ambiguousIdentifier()?.GetText() ?? called.baseType().GetText();
            // Measured for CBool, Len and VarType (#1691), so a call to any of VB's functions reads 0 there; a call to any
            // other name is Null, which that compiler's constants leave undetermined.
            if (callsReadZero)
                return Functions.Contains(name) || VB6BuiltIns.TryGetBuiltIn(name, out _) ? new Vb6Value(0).AsVariant() : Vb6Value.Null;
            if (!Functions.Contains(name))
                throw new VBCompileErrorException("Variable not defined");
            return Call(name, Argument(called, lookup), called);
        }
        throw Unsupported(call);
    }

    // The one argument every function here takes.
    private static Vb6Value Argument(VB6Parser.ICS_S_ProcedureOrArrayCallContext called, Func<string, Vb6Value> lookup)
    {
        if (called.argsCall() is not [var args] || args.argCall() is not [var only] || args.COMMA().Length > 0
            || args.SEMICOLON().Length > 0)
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
        if (only.BYVAL() != null || only.BYREF() != null || only.PARAMARRAY() != null)
            throw Unsupported(only);
        return Value(only.valueStmt(), lookup, callsReadZero: false);
    }

    private static Vb6Value Call(string name, Vb6Value argument, VB6Parser.ICS_S_ProcedureOrArrayCallContext call)
    {
        if (name.Equals("CVar", StringComparison.OrdinalIgnoreCase))
            return argument.AsVariant();
        if (name.Equals("Len", StringComparison.OrdinalIgnoreCase) || name.Equals("LenB", StringComparison.OrdinalIgnoreCase))
        {
            // Len(5) is "Variable required - can't assign to this expression", as in running code (measured). A constant is a
            // Variant, so Len(Foo) reads its text.
            if (argument.HasFixedType && argument.Type != Vb6Value.ValueType.String)
                throw new VBCompileErrorException("Variable required - can't assign to this expression");
            if (name.Equals("LenB", StringComparison.OrdinalIgnoreCase))
                return argument.Type == Vb6Value.ValueType.String
                    ? new Vb6Value((long)((string)argument.Value!).Length * 2)
                    : throw new NotImplementedException($"LenB of anything but a String, as in {call.GetText()}, is not implemented (#1112)");
        }
        return VB6BuiltIns.TryGetBuiltIn(name, out var function)
            ? function(null!, [argument], false)
            : throw new NotImplementedException($"{name} in an #If or #Const is not implemented (#1112)");
    }

    private static Vb6Value Compare(VB6Parser.VsCompareContext compare, Vb6Value left, Vb6Value right)
    {
        // Null on either side is Null, as it is in running code, and so "Invalid use of Null" where an #If reads it (measured).
        if (compare.LIKE() != null)
            return left.IsNull || right.IsNull ? Vb6Value.Null
                : new Vb6Value(VbLike.Matches(LikeOperand(left), LikeOperand(right), textCompare: true));
        if (compare.IS() != null)
            throw new VBCompileErrorException("Type mismatch");   // measured, 1 Is 1
        if (left.IsNull || right.IsNull)
            return Vb6Value.Null;
        var order = Order(left, right, compare);
        return new Vb6Value(compare.EQ() != null ? order == 0
            : compare.NEQ() != null ? order != 0
            : compare.LT() != null ? order < 0
            : compare.GT() != null ? order > 0
            : compare.LEQ() != null ? order <= 0
            : order >= 0);
    }

    /// <summary>
    /// How two values order in an <c>#If</c> (measured, #1112). Two Strings compare as text and two numbers as numbers. A
    /// String against a number depends on which is the Variant, a constant being one and a literal not:
    /// <list type="bullet">
    /// <item>a typed String against a Variant number compares as text, so <c>#Const N5 = 5</c> then <c>N5 &lt; "10"</c> is false;</item>
    /// <item>a typed String against a typed number reads the String as a number, so <c>5 &lt; "10"</c> is true and <c>0 = ""</c>
    /// "Type mismatch";</item>
    /// <item>a Variant String against a typed number compares as numbers where the String reads as one, so <c>#Const S5 = "5"</c>
    /// then <c>S5 &lt; 10</c> is true, and otherwise puts the number first: <c>5 &lt; SA</c> is true for <c>SA</c> "a";</item>
    /// <item>a Variant String against a Variant number puts the number first, so <c>N5 &lt; S5</c> is true and <c>N5 = S5</c> false.</item>
    /// </list>
    /// <c>Empty</c> against a String is the empty String: <c>Empty = ""</c> is true.
    /// </summary>
    private static int Order(Vb6Value left, Vb6Value right, VB6Parser.VsCompareContext compare)
    {
        var leftText = left.Type == Vb6Value.ValueType.String;
        var rightText = right.Type == Vb6Value.ValueType.String;
        if (leftText && rightText)
            return Text((string)left.Value!, (string)right.Value!);
        if (!leftText && !rightText)
            return VbNumeric.CompareNumbers(left, right, compare);
        return leftText ? TextAgainstNumber(left, right, compare) : -TextAgainstNumber(right, left, compare);
    }

    // Where text orders against number: below zero when text is the lesser.
    private static int TextAgainstNumber(Vb6Value text, Vb6Value number, VB6Parser.VsCompareContext compare)
    {
        var s = (string)text.Value!;
        if (number.Type == Vb6Value.ValueType.EmptyVariant)
            return Text(s, "");
        if (!text.HeldAsVariant)
            return number.HeldAsVariant
                ? Text(s, VbDisplay.ToText(number))
                : VbNumeric.CompareNumbers(AsNumber(text, number, compare), number, compare);
        if (!number.HeldAsVariant && TryAsNumber(s, out var read))
            return VbNumeric.CompareNumbers(read, number, compare);
        return 1;
    }

    // A typed String read as the typed number it is compared with: as a date beside a Date, and otherwise as CDbl reads it.
    private static Vb6Value AsNumber(Vb6Value text, Vb6Value number, VB6Parser.VsCompareContext compare)
    {
        try
        {
            return number.Type == Vb6Value.ValueType.Date ? VB6BuiltIns.CDate(text) : new Vb6Value(VB6BuiltIns.ToNum(text));
        }
        catch (VBRunTimeException e)
        {
            throw new VBRunTimeException(compare, e.Error);
        }
    }

    private static bool TryAsNumber(string s, out Vb6Value number)
    {
        try
        {
            number = new Vb6Value(VB6BuiltIns.ToNum(new Vb6Value(s)));
            return true;
        }
        catch (VBRunTimeException)
        {
            number = default;
            return false;
        }
    }

    // vb6.exe's text order, measured (#1567), and measured in a directive too (#1598).
    private static int Text(string left, string right) => Math.Sign(VbTextCompare.Compare(left, right));

    private static string LikeOperand(Vb6Value value) => VbDisplay.ToText(VB6BuiltIns.RefuseNonScalar(value));

    private static NotImplementedException Unsupported(ParserRuleContext node) =>
        new($"`{node.GetText()}` in an #If or #Const is not implemented (#1112)");

    private sealed class Refusal : IAntlrErrorListener<int>
    {
        internal static readonly Refusal Instance = new();

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
            string msg, RecognitionException e) => throw new ArgumentException(msg);
    }
}
