using System.Runtime.InteropServices;
using HexIDE.Language;

namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>Reads a module's <c>#If</c>s as the interpreter does, with its parser and its operators, for the IDE to grey.</summary>
public sealed class InterpreterConditionalCode : IConditionalCodeReader
{
    string? IConditionalCodeReader.DefaultTarget(ProjectDefinition project) => ProjectTargets.Of(project) == ProjectTarget.RDCore ? "rdcore" :
        project.VbaBitness switch { VbaBitness.Bit32 => "vba32", VbaBitness.Bit64 => "vba64", _ => null };

    public IReadOnlyList<LineRange> InactiveLines(string source, ConditionalConstants constants) =>
        InactiveLines(source, constants, host: null);

    internal IReadOnlyList<LineRange> InactiveLines(string source, ConditionalConstants constants, OSPlatform? host)
    {
        var builtIns = constants.BuiltIns ?? TargetBuiltIns(constants, host);
        var compilation = new CompilationConstants(new CompilationArguments(constants.Arguments), Variants(builtIns))
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

    internal static IReadOnlyDictionary<string, object?>? TargetBuiltIns(ConditionalConstants constants, OSPlatform? host)
    {
        var table = constants.Target switch
        {
            "vba32" => PredefinedCompilationConstants.ForTarget(ProjectTarget.None, VbaBitness.Bit32, host),
            "vba64" => PredefinedCompilationConstants.ForTarget(ProjectTarget.None, VbaBitness.Bit64, host),
            "rdcore" => PredefinedCompilationConstants.ForTarget(ProjectTarget.RDCore, VbaBitness.None, host, constants.Win64OnlySetOnWindows),
            _ => null,
        };
        return table?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);
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
