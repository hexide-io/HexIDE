using HexIDE.Language;

namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>
/// The interpreter's conditional compilation (#1112): <see cref="HexIDE.Language.ConditionalCompilation"/>'s scanner, which
/// the language server shares, with the expressions read by the interpreter's own parser and valued by its own operators
/// (<see cref="ConstantExpression"/>). It always chooses a branch, so it never reports an undetermined one.
/// </summary>
public static class ConditionalCompiler
{
    /// <summary>
    /// <paramref name="source"/> with its conditional compilation applied under <paramref name="constants"/>. A directive's
    /// error is a compile error, raised here, as the module loads and before anything runs, naming
    /// <paramref name="moduleName"/> and the line.
    /// </summary>
    public static PreprocessedSource Preprocess(string source, CompilationConstants constants, string? moduleName = null)
    {
        try
        {
            return HexIDE.Language.ConditionalCompilation.Preprocess(source, new Evaluator(constants));
        }
        catch (ConditionalCompilationException e)
        {
            throw new VBCompileErrorException($"{e.Message} in {(moduleName is null ? "" : moduleName + ", ")}line {e.Line}") { Line = e.Line };
        }
    }

    private sealed class Evaluator(CompilationConstants constants) : IConstantEvaluator<VB6Parser.ValueStmtContext, Vb6Value>
    {
        public VB6Parser.ValueStmtContext? Parse(string text) => ConstantExpression.TryParse(text);

        public Vb6Value Evaluate(VB6Parser.ValueStmtContext expression, Func<string, Vb6Value> lookup) =>
            Reported(() => ConstantExpression.Evaluate(expression, lookup));

        public bool? Holds(Vb6Value condition) => Reported(() => ConstantExpression.Holds(condition));

        public Vb6Value Constant(Vb6Value value) => value.AsVariant();

        // An argument or a built-in, or else a Variant holding 0 (measured: CStr of an undefined name is "0").
        public Vb6Value Name(string name) => constants.TryGet(name, out var value) ? value : new Vb6Value(0).AsVariant();

        private static T Reported<T>(Func<T> evaluate)
        {
            try
            {
                return evaluate();
            }
            catch (VBCompileErrorException e) when (e.Line is null)
            {
                const string prefix = "Compile error:\n\n";
                throw new DirectiveEvaluationException(e.Message.StartsWith(prefix, StringComparison.Ordinal) ? e.Message[prefix.Length..] : e.Message);
            }
        }
    }
}
