using HexIDE.Language;

namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>Reads a module's <c>#If</c>s as the interpreter does, with its parser and its operators, for the IDE to grey.</summary>
public sealed class InterpreterConditionalCode : IConditionalCodeReader
{
    public IReadOnlyList<LineRange> InactiveLines(string source, ConditionalConstants constants)
    {
        var compilation = new CompilationConstants(new CompilationArguments(constants.Arguments), Variants(constants.BuiltIns))
        {
            CallsReadZero = constants.CallsReadZero,
            NullIsUndetermined = constants.NullIsUndetermined,
        };
        try
        {
            var result = ConditionalCompiler.Preprocess(source, compilation);
            return result.InactiveLines.Where(range =>
                !result.UndeterminedLines.Any(unknown => unknown.First <= range.Last && unknown.Last >= range.First)).ToArray();
        }
        catch (VBCompileErrorException)
        {
            return [];
        }
    }

    private static Dictionary<string, Vb6Value>? Variants(IReadOnlyDictionary<string, object?>? builtIns)
    {
        if (builtIns is null) return null;
        var variants = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var (constant, value) in builtIns)
            variants[constant] = value switch
            {
                int number => new Vb6Value(number).AsVariant(),
                string s => new Vb6Value(s).AsVariant(),
                _ => Vb6Value.Null,
            };
        return variants;
    }
}
