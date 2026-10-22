namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Named arguments, `Foo c:=5, a:=1` (#1465). Measured with vb6.exe, native and p-code identical (the oracle's "Named
/// arguments"): a named argument goes to the parameter of its name, in any case, after the positional ones; and the
/// arguments are evaluated in <b>parameter</b> order, not the order written: `Foo c:=F("c"), a:=F("a")` evaluates a first,
/// as `Mid(Start:=F(…), String:=F(…))` evaluates String first. So a named argument reaches here unevaluated
/// (<see cref="CallArg.Deferred"/>), and is evaluated once it has been placed.
/// </summary>
internal static class NamedArguments
{
    public static bool Any(IReadOnlyList<CallArg> args)
    {
        foreach (var arg in args)
            if (arg.Name is not null)
                return true;
        return false;
    }

    /// <summary>
    /// The arguments in parameter order, a blank or omitted one Missing, each named one evaluated in that order. VB6's
    /// compile errors are raised before any named one is evaluated, as the statement runs (measured): a name no parameter has
    /// is "Named argument not found", one given twice or naming a parameter a positional argument already filled "Named
    /// argument already specified", a ParamArray "Argument in ParamArray may not be named", a required parameter left out
    /// <paramref name="notOptional"/>, and more positional arguments than parameters "Wrong number of arguments or invalid
    /// property assignment", as it is with none named.
    /// </summary>
    public static async Task<IReadOnlyList<CallArg>> Arrange(IReadOnlyList<CallArg> args, IReadOnlyList<string> parameters,
        Func<int, bool> isRequired, Func<int, bool>? isParamArray = null, string notOptional = "Argument not optional")
    {
        if (!Any(args))
            return args;
        var placed = new CallArg?[parameters.Count];
        var named = 0;
        while (named < args.Count && args[named].Name is null)
            named++;
        // A ParamArray takes the rest, but binding does not take a ParamArray yet, so those are not kept.
        if (named > placed.Length && !(placed.Length > 0 && isParamArray?.Invoke(placed.Length - 1) == true))
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
        for (var i = 0; i < named && i < placed.Length; i++)
            placed[i] = args[i];
        for (var i = named; i < args.Count; i++)
        {
            var index = IndexOf(parameters, args[i].Name!);
            if (index < 0)
                throw new VBCompileErrorException("Named argument not found");
            if (isParamArray?.Invoke(index) == true)
                throw new VBCompileErrorException("Argument in ParamArray may not be named");
            if (placed[index] is not null)
                throw new VBCompileErrorException("Named argument already specified");
            placed[index] = args[i];
        }
        for (var p = 0; p < placed.Length; p++)
            if (placed[p] is null && isRequired(p))
                throw new VBCompileErrorException(notOptional);
        var last = placed.Length - 1;
        while (last >= 0 && placed[last] is null)
            last--;
        var arranged = new List<CallArg>(last + 1);
        for (var p = 0; p <= last; p++)
            arranged.Add(placed[p] is { Deferred: { } evaluate } ? await evaluate() : placed[p] ?? new CallArg(Vb6Value.Missing, null));
        return arranged;
    }

    private static int IndexOf(IReadOnlyList<string> parameters, string name)
    {
        for (var p = 0; p < parameters.Count; p++)
            if (string.Equals(parameters[p], name, StringComparison.OrdinalIgnoreCase))
                return p;
        return -1;
    }
}
