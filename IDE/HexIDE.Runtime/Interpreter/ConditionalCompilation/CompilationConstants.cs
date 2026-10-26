namespace HexIDE.Runtime.Interpreter.ConditionalCompilation;

/// <summary>
/// What an <c>#If</c> can name besides its own module's <c>#Const</c>s: the project's arguments, then the compiler's built-in
/// constants (#1112). A module's <c>#Const</c> shadows both, and an argument shadows a built-in: <c>CondComp="Win32 = 0"</c>
/// makes <c>Win32</c> false (measured). A name none of them defines reads as a Variant holding 0.
/// </summary>
public sealed class CompilationConstants
{
    /// <summary>
    /// vb6.exe's built-ins, measured: <c>Win32</c> is 1, and <c>Win32 = True</c> false, and <c>Win16</c> is 0. <c>VBA6</c>,
    /// <c>VBA7</c>, <c>Win64</c> and <c>Mac</c> are not defined, so they read 0. Every personality takes these for now: VBA's
    /// documented set (<c>VBA7</c> = 1) would select declarations HexIDE cannot parse (#1548), and is used only for the
    /// editor preview (#1113).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, short> Vb6BuiltIns =
        new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase) { ["Win16"] = 0, ["Win32"] = 1 };

    // VBA's constants, not measured here: there is no VBA oracle. Preview only (#1113). Microsoft's page calls them
    // True, but VB6 (above) and twinBASIC (#1691) both measure 1 where their own documentation says True, so 1 it is.
    public static readonly IReadOnlyDictionary<string, short> Vba32BuiltIns =
        new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase)
        { ["VBA6"] = 1, ["VBA7"] = 1, ["Win32"] = 1 };

    public static readonly IReadOnlyDictionary<string, short> Vba64BuiltIns =
        new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase)
        { ["VBA6"] = 1, ["VBA7"] = 1, ["Win32"] = 1, ["Win64"] = 1 };

    /// <summary>A program with no project arguments, under VB6's built-ins.</summary>
    public static readonly CompilationConstants Vb6 = new(CompilationArguments.None);

    public CompilationConstants(CompilationArguments arguments, IReadOnlyDictionary<string, short>? builtIns = null)
    {
        Arguments = arguments;
        BuiltIns = builtIns ?? Vb6BuiltIns;
    }

    public CompilationArguments Arguments { get; }

    public IReadOnlyDictionary<string, short> BuiltIns { get; }

    /// <summary>A project's constants: its <c>CondComp</c>, or vb6.exe's refusal of it, under VB6's built-ins.</summary>
    public static CompilationConstants ForProject(ProjectDefinition project) => new(CompilationArguments.ForProject(project));

    /// <summary>An argument's or a built-in's value, as the Variant a constant is.</summary>
    internal bool TryGet(string name, out Vb6Value value)
    {
        if (Arguments.Values.TryGetValue(name, out var number) || BuiltIns.TryGetValue(name, out number))
        {
            value = new Vb6Value((int)number).AsVariant();
            return true;
        }
        value = default;
        return false;
    }
}
