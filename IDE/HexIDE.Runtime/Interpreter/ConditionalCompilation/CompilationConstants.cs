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
    public static readonly IReadOnlyDictionary<string, Vb6Value> Vb6BuiltIns = Variants(("Win16", 0), ("Win32", 1));

    // VBA's constants, not measured here: there is no VBA oracle. Preview only (#1113). Microsoft's page calls them
    // True, but VB6 (above) measures 1 where its own documentation says True, and so does #1691's, so 1 it is.
    public static readonly IReadOnlyDictionary<string, Vb6Value> Vba32BuiltIns = Variants(("VBA6", 1), ("VBA7", 1), ("Win32", 1));

    public static readonly IReadOnlyDictionary<string, Vb6Value> Vba64BuiltIns =
        Variants(("VBA6", 1), ("VBA7", 1), ("Win32", 1), ("Win64", 1));

    /// <summary>A program with no project arguments, under VB6's built-ins.</summary>
    public static readonly CompilationConstants Vb6 = new(CompilationArguments.None);

    /// <summary>
    /// <paramref name="builtIns"/> are Variants, so another compiler's can be a String or a Long, as #1691's are; VB6's are
    /// all numbers. A built-in held as Null is one that compiler defines but the project does not record.
    /// </summary>
    public CompilationConstants(CompilationArguments arguments, IReadOnlyDictionary<string, Vb6Value>? builtIns = null)
    {
        Arguments = arguments;
        BuiltIns = builtIns ?? Vb6BuiltIns;
    }

    public CompilationArguments Arguments { get; }

    public IReadOnlyDictionary<string, Vb6Value> BuiltIns { get; }

    /// <summary>
    /// A function call in an <c>#If</c> reads as 0, whatever it names, rather than being called. vb6.exe calls the
    /// conversions and <c>Len</c>; #1691's compiler reads every call as 0, so <c>Not CBool(Win64)</c> holds at 64-bit too.
    /// </summary>
    public bool CallsReadZero { get; init; }

    /// <summary>
    /// A condition that comes to Null leaves its branches undetermined, so nothing is greyed on a guess. vb6.exe refuses
    /// Null as "Invalid use of Null"; here Null stands for a built-in the project does not record.
    /// </summary>
    public bool NullIsUndetermined { get; init; }

    /// <summary>A project's constants: its <c>CondComp</c>, or vb6.exe's refusal of it, under VB6's built-ins.</summary>
    public static CompilationConstants ForProject(ProjectDefinition project) => new(CompilationArguments.ForProject(project));

    /// <summary>An argument's or a built-in's value, as the Variant a constant is.</summary>
    internal bool TryGet(string name, out Vb6Value value)
    {
        if (Arguments.Values.TryGetValue(name, out var number))
        {
            value = new Vb6Value((int)number).AsVariant();
            return true;
        }
        return BuiltIns.TryGetValue(name, out value);
    }

    private static Dictionary<string, Vb6Value> Variants(params (string Name, int Value)[] constants)
    {
        var table = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in constants) table[name] = new Vb6Value(value).AsVariant();
        return table;
    }
}
