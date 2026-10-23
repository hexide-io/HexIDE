using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

// IIf and Choose (#1461, #1464): the two intrinsics that pick one of their arguments. Measured with vb6.exe under en-GB,
// native and p-code identical (the oracle's "IIf and Choose"). Each takes its choices as themselves, so an object comes
// back as the object (`Set o = IIf(True, c, Nothing)`), and reads only its first argument as a value.
public partial class VB6BuiltIns
{
    private static void RegisterChoice(Dictionary<string, BuiltinFn> d)
    {
        // Both parts are evaluated before IIf is called, as every argument is, so `IIf(n <> 0, 1 / n, 0)` with n 0 raises
        // 11 (measured). The condition is read as CBool reads it, except that Null is False, with no error: `IIf("abc", …)`
        // is 13, `IIf("0", …)`, `IIf(Empty, …)` and `IIf(Null, …)` the false part, and `IIf(0.4, …)` the true part. The
        // part comes back as a Variant: `IIf(True, i, 6) * 7000`, with i an Integer 5, is 35000.
        d["IIf"] = (_, a, _) =>
        {
            if (a.Count < 3 || !Supplied(a, 1))
                throw new VBCompileErrorException("Argument not optional");
            if (a.Count > 3)
                throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
            var condition = !a[0].IsNull && CBool(a[0]).Value is true;
            return (condition ? a[1] : a[2]).AsVariant();
        };
        // The index is truncated, not rounded: `Choose(1.6, "a", "b", "c")` is "a" and `Choose(2.5, …)` "b". One outside
        // the choices is Null, with no error, however far outside (`Choose(0, …)`, `Choose(True, …)` with True -1, and
        // `Choose(1E+20, …)` are Null), and `Choose(1)`, with no choices at all, is Null too. A Null index is 94, and one
        // that is not a number 13. Every choice is evaluated, in order, before Choose is called (measured).
        d["Choose"] = (_, a, _) =>
        {
            if (a.Count == 0 || !Supplied(a, 0))
                throw new VBCompileErrorException("Argument not optional");
            var index = Math.Truncate(ToNum(a[0]));
            return index >= 1 && index <= a.Count - 1 ? a[(int)index].AsVariant() : Vb6Value.Null;
        };
        // Switch(expr1, value1[, expr2, value2, ...]) is the value after the first expression that holds, or Null where none
        // does (#1560, measured). Every argument is evaluated before Switch is called, values as well as expressions. An odd
        // count is 5, one argument included. Each expression is read as CBool reads it, until one holds: "True", 2 and a Date
        // hold, "x" and an array are 13, and a Null or Empty one does not hold and raises nothing. One after the expression
        // that held is not read, so `Switch(True, 1, "x", 2)` is 1. The value comes back as itself, a Null included.
        d["Switch"] = (_, a, _) =>
        {
            if (a.Count == 0 || a.Count % 2 != 0)
                throw InvalidCall();
            for (var i = 0; i < a.Count; i += 2)
                if (!a[i].IsNull && CBool(a[i]).Value is true)
                    return a[i + 1].AsVariant();
            return Vb6Value.Null;
        };
    }
}
