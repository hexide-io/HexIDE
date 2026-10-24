// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Conditional compilation for the server's diagnostics (#1112): the interpreter's scanner, linked from HexIDE.Core, with a
// deliberately reduced evaluator that says when it cannot answer.

using Antlr4.Runtime;
using HexIDE.Language;

namespace HexIDE.VbLspServer;

/// <summary>
/// What conditional compilation knows about the project a document belongs to: its Conditional Compilation Arguments, or
/// <see cref="NoProject"/> where no <c>.vbp</c> lists the document, so an argument it might define cannot be known.
/// </summary>
public sealed record VbCompilationContext(IReadOnlyDictionary<string, short>? Arguments)
{
    /// <summary>Class and module names listed by the document's project; no cross-file binding.</summary>
    public IReadOnlyCollection<string> BaselineNames { get; init; } = [];

    /// <summary>A document no project lists: only VB6's built-ins are known.</summary>
    public static readonly VbCompilationContext NoProject = new((IReadOnlyDictionary<string, short>?)null);
}

/// <summary>
/// A module's directives applied before the server parses it (#1112), so its diagnostics skip code vb6.exe never compiles:
/// an inactive branch is made empty and never parsed, as in vb6.exe.
/// <para>
/// The scanner is the interpreter's (<see cref="ConditionalCompilation"/>), compiled into both, so the two cannot read a
/// directive differently. The evaluator is not: the interpreter values an expression with its own runtime, which the server
/// does not have. This one values whole numbers, <c>True</c> and <c>False</c>, names, parentheses, unary minus, <c>Not</c>,
/// <c>And</c>, <c>Or</c>, <c>Xor</c>, <c>Eqv</c>, <c>Imp</c> and the six comparisons, with VB6's semantics for that subset:
/// <c>Not</c> is bitwise, so <c>Not 1</c> is -2 and true. Anything else makes a condition undetermined: a string, a
/// fraction, arithmetic, a function, <c>Like</c>, or a name that is no <c>#Const</c> or built-in in a document no project
/// lists. A block with an undetermined condition keeps one branch for the parser, and reports no diagnostics anywhere in it,
/// so the server never squiggles code vb6.exe may not compile.
/// </para>
/// <para>
/// Which branch matters, because this grammar's recovery from an error can swallow every error after it: a kept
/// <c>Declare PtrSafe</c> would leave the rest of the module looking clean. So the first branch that might be the one is kept,
/// and where the parse's first error is inside an undetermined block, the module is parsed again keeping the last instead,
/// and the parse whose first error comes later answers (<see cref="VbDiagnosticsProvider"/>).
/// </para>
/// </summary>
internal static class VbConditionalCompilation
{
    /// <summary>The text to parse, the lines whose diagnostics are held back, and a directive's own error if it had one.</summary>
    internal sealed record Applied(string Text, IReadOnlyList<LineRange> Undetermined, ConditionalCompilationException? Error)
    {
        public bool HoldsBack(int line) => Error is not null || Undetermined.Any(r => r.Contains(line));
    }

    internal static Applied Apply(string source, VbCompilationContext context, UndeterminedBranch keep = UndeterminedBranch.First)
    {
        try
        {
            var applied = ConditionalCompilation.Preprocess(source, new Evaluator(context), keep);
            return new Applied(applied.Text, applied.UndeterminedLines, null);
        }
        catch (ConditionalCompilationException e)
        {
            // vb6.exe refuses the module at the directive. The module is read as written, so the outline still has
            // something to show, and the directive's error is the one diagnostic reported.
            return new Applied(source, [], e);
        }
    }

    /// <summary>The directive's error as a diagnostic over its whole line.</summary>
    internal static LspDiagnostic Diagnostic(ConditionalCompilationException error, string source)
    {
        var lines = source.Split('\n');
        var text = error.Line - 1 < lines.Length ? lines[error.Line - 1].TrimEnd('\r') : "";
        return new LspDiagnostic(new LspRange(new LspPosition(error.Line - 1, 0), new LspPosition(error.Line - 1, text.Length)),
            error.Message, 1);
    }

    // A value is a whole number, True being -1 and False 0, or null where it cannot be known.
    private sealed class Evaluator(VbCompilationContext context) : IConstantEvaluator<VisualBasic6Parser.ValueStmtContext, long?>
    {
        public VisualBasic6Parser.ValueStmtContext? Parse(string text)
        {
            if (text.Length == 0)
                return null;
            var lexer = new VisualBasic6Lexer(new AntlrInputStream(text));
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(Refusal.Instance);
            var parser = new VisualBasic6Parser(new CommonTokenStream(lexer)) { ErrorHandler = new BailErrorStrategy() };
            parser.RemoveErrorListeners();
            parser.AddParseListener(new ParseDepthGuard(64));
            try
            {
                var expression = parser.valueStmt();
                return parser.CurrentToken.Type == TokenConstants.EOF ? expression : null;
            }
            catch (Exception)   // the bail, the lexer's refusal, or the depth guard: not an expression
            {
                return null;
            }
        }

        public long? Evaluate(VisualBasic6Parser.ValueStmtContext expression, Func<string, long?> lookup) => Value(expression, lookup);

        public bool? Holds(long? condition) => condition is { } n ? n != 0 : null;

        public long? Constant(long? value) => value;

        // An argument, a built-in, or, in a document a project lists, 0 (measured: an undefined name reads 0 to vb6.exe).
        public long? Name(string name) =>
            context.Arguments is { } arguments && arguments.TryGetValue(name, out var argument) ? argument
            : name.Equals("Win32", StringComparison.OrdinalIgnoreCase) ? 1
            : name.Equals("Win16", StringComparison.OrdinalIgnoreCase) ? 0
            : context.Arguments is not null ? 0
            : null;

        private static long? Value(VisualBasic6Parser.ValueStmtContext node, Func<string, long?> lookup)
        {
            long? Of(VisualBasic6Parser.ValueStmtContext operand) => Value(operand, lookup);
            static long? Both(long? l, long? r, Func<long, long, long> op) => l is { } a && r is { } b ? op(a, b) : null;
            return node switch
            {
                VisualBasic6Parser.VsLiteralContext literal => Literal(literal.literal()),
                VisualBasic6Parser.VsStructContext group when group.valueStmt().Length == 1 => Of(group.valueStmt(0)),
                VisualBasic6Parser.VsPlusMinusContext sign => sign.MINUS() != null ? -Of(sign.valueStmt()) : Of(sign.valueStmt()),
                VisualBasic6Parser.VsNotContext not => ~Of(not.valueStmt()),
                VisualBasic6Parser.VsCompContext compare => Compare(compare, Of(compare.valueStmt(0)), Of(compare.valueStmt(1))),
                VisualBasic6Parser.VsAndContext and => Both(Of(and.valueStmt(0)), Of(and.valueStmt(1)), static (a, b) => a & b),
                VisualBasic6Parser.VsOrContext or => Both(Of(or.valueStmt(0)), Of(or.valueStmt(1)), static (a, b) => a | b),
                VisualBasic6Parser.VsXorContext xor => Both(Of(xor.valueStmt(0)), Of(xor.valueStmt(1)), static (a, b) => a ^ b),
                VisualBasic6Parser.VsEqvContext eqv => Both(Of(eqv.valueStmt(0)), Of(eqv.valueStmt(1)), static (a, b) => ~(a ^ b)),
                VisualBasic6Parser.VsImpContext imp => Both(Of(imp.valueStmt(0)), Of(imp.valueStmt(1)), static (a, b) => ~a | b),
                VisualBasic6Parser.VsICSContext name
                    when name.implicitCallStmt_InStmt().iCS_S_VariableOrProcedureCall() is { } bare
                         && bare.typeHint() is null && bare.dictionaryCallStmt() is null
                    => lookup(bare.ambiguousIdentifier().GetText()),
                _ => null,
            };
        }

        private static long? Compare(VisualBasic6Parser.VsCompContext compare, long? left, long? right)
        {
            if (left is not { } l || right is not { } r || compare.LIKE() != null || compare.IS() != null)
                return null;
            var holds = compare.EQ() != null ? l == r
                : compare.NEQ() != null ? l != r
                : compare.LT() != null ? l < r
                : compare.GT() != null ? l > r
                : compare.LEQ() != null ? l <= r
                : l >= r;
            return holds ? -1 : 0;
        }

        // A whole-number literal, an Integer or a Long as VB6 types it, True, False or Empty. Anything else is not known here.
        private static long? Literal(VisualBasic6Parser.LiteralContext literal)
        {
            if (literal.TRUE() != null)
                return -1;
            if (literal.FALSE() != null || literal.EMPTY_() != null)
                return 0;
            if (literal.integerLiteral() is not { } number)
                return null;
            var text = number.INTEGERLITERAL().GetText();
            var suffix = text[^1] is '%' or '&' ? text[^1] : '\0';
            var digits = suffix == '\0' ? text : text[..^1];
            if (digits.Any(c => c is < '0' or > '9') || !long.TryParse(digits, out var value))
                return null;
            if (number.MINUS().Length % 2 == 1)
                value = -value;
            return suffix switch
            {
                '%' => value is >= short.MinValue and <= short.MaxValue ? value : null,
                _ => value is >= int.MinValue and <= int.MaxValue ? value : null,   // beyond a Long, VB6 makes a Double
            };
        }
    }

    private sealed class Refusal : IAntlrErrorListener<int>
    {
        internal static readonly Refusal Instance = new();

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
            string msg, RecognitionException e) => throw new ArgumentException(msg);
    }
}
