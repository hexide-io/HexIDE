using System.Diagnostics.CodeAnalysis;

namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>
/// A project's Conditional Compilation Arguments, as Project Properties' Make tab writes them: the <c>.vbp</c>'s
/// <c>CondComp</c> line, such as <c>CondComp="DEBUG = 1 : TRACE = 0"</c> (#1112). What vb6.exe accepts there is
/// <see cref="HexIDE.Language.ConditionalCompilation.TryParseArguments"/>'s, which the language server reads with too.
/// The line is not modelled by the project reader: it round-trips verbatim as an unknown pre-section line, and is read here.
/// </summary>
public sealed class CompilationArguments
{
    /// <summary>The <c>.vbp</c> key.</summary>
    public const string ProjectKey = "CondComp";

    /// <summary>vb6.exe's message for a line it refuses.</summary>
    public const string InvalidSyntax = "Invalid syntax for constant declarations";

    /// <summary>A project with no arguments.</summary>
    public static readonly CompilationArguments None = new(new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase));

    private CompilationArguments(IReadOnlyDictionary<string, short> values) => Values = values;

    /// <summary>Each argument's value, by its name in any case.</summary>
    public IReadOnlyDictionary<string, short> Values { get; }

    /// <summary>The arguments a <c>CondComp</c> value gives, without its quotes, or false where vb6.exe refuses it.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out CompilationArguments? arguments)
    {
        arguments = HexIDE.Language.ConditionalCompilation.TryParseArguments(text, out var values) ? new CompilationArguments(values) : null;
        return arguments is not null;
    }

    /// <summary>
    /// The project's arguments, or vb6.exe's refusal, which stops the program before it runs. Measured: <c>/make</c> builds
    /// nothing, and the IDE says "Invalid syntax for constant declarations" as it opens the project, and <c>/runexit</c> then
    /// runs nothing.
    /// </summary>
    public static CompilationArguments ForProject(ProjectDefinition project)
    {
        var text = AppInfo.PreSectionValue(project, ProjectKey);
        return TryParse(text, out var arguments)
            ? arguments
            : throw new VBCompileErrorException($"{InvalidSyntax}: the project's Conditional Compilation Arguments, {ProjectKey}=\"{text}\"");
    }
}
