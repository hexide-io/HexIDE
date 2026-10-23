using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>
/// A project's Conditional Compilation Arguments, as Project Properties' Make tab writes them: the <c>.vbp</c>'s
/// <c>CondComp</c> line, such as <c>CondComp="DEBUG = 1 : TRACE = 0"</c> (#1112). Measured with <c>vb6.exe /make</c>, native
/// and p-code alike (the oracle's "Conditional compilation"):
/// <list type="bullet">
/// <item>pairs of <c>name = n</c> separated by colons, the spaces optional, and the names in any case;</item>
/// <item><c>n</c> a whole number from -32768 to 32767, with an optional sign;</item>
/// <item>anything else refuses the whole line: 70000, <c>&amp;H10</c>, <c>True</c>, 1.5, a string, a semicolon for the
/// colon, a name given twice, an empty value, or a colon at the end. <c>/make</c> then builds nothing.</item>
/// </list>
/// The line is not modelled by the project reader: it round-trips verbatim as an unknown pre-section line, and is read here.
/// </summary>
public sealed partial class CompilationArguments
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

    /// <summary>
    /// The arguments a <c>CondComp</c> value gives, without its quotes, or false where vb6.exe refuses it. No value, or an
    /// empty one, is no arguments (measured: <c>CondComp=""</c> builds).
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out CompilationArguments? arguments)
    {
        arguments = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            arguments = None;
            return true;
        }
        var values = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split(':'))
        {
            if (Pair().Match(part) is not { Success: true } pair
                || !short.TryParse(pair.Groups["value"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
                || !values.TryAdd(pair.Groups["name"].Value, value))
                return false;
        }
        arguments = new CompilationArguments(values);
        return true;
    }

    /// <summary>The project's arguments, or vb6.exe's refusal, which stops the program before it runs as <c>/make</c> does.</summary>
    public static CompilationArguments ForProject(ProjectDefinition project)
    {
        var text = AppInfo.PreSectionValue(project, ProjectKey);
        return TryParse(text, out var arguments)
            ? arguments
            : throw new VBCompileErrorException($"{InvalidSyntax}: the project's Conditional Compilation Arguments, {ProjectKey}=\"{text}\"");
    }

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*=\s*(?<value>[+-]?[0-9]+)\s*$")]
    private static partial Regex Pair();
}
