using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.Runtime.Interpreter;

// Environ and Error (#1560), measured with vb6.exe under en-GB, native and p-code alike (the oracle's "StrComp, StrConv,
// Environ, Switch and Error()").
public partial class VB6BuiltIns
{
    /// <summary>The Err object that Error, given no number, reads. Set by the interpreter that owns these builtins.</summary>
    internal Func<VbErr>? ErrObject { get; set; }

    private static void RegisterInteraction(Dictionary<string, BuiltinFn> d)
    {
        d["Shell"] = (_, a, _) => Shell(a);
        d["Environ"] = (_, a, _) => new Vb6Value(Environ(a[0]));
        d["Error"] = (self, a, _) => new Vb6Value(ErrorText(self, a));
    }

    /// <summary>
    /// Environ(name) is the variable's value, "" where there is none; Environ(n) is the n-th variable as "NAME=value", ""
    /// past the last. Measured: a String is a name, even "1", and a name ignores spaces around it and its case, so "path"
    /// and " PATH" are both PATH, while "" is 5. A number is read as CInt reads it, 1.5 being 2: 0, a negative, True, Empty
    /// and 256 up are 5, and past an Integer 6. 255 is the bound, not the count: Environ(255) past the last is "". Null is
    /// 94. The variables are counted in order of name, as vb6.exe gave them (no two neighbours out of order), and the
    /// Windows-only ones whose names start with "=" are not counted (measured: Environ(1) holds its "=" after a name).
    /// </summary>
    private static string Environ(Vb6Value expression)
    {
        if (expression.IsNull)
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        if (expression.Type == Vb6Value.ValueType.String)
        {
            var name = AsStr(expression).Trim(' ');
            if (name.Length == 0)
                throw InvalidCall();
            return Variables().FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)).Value ?? "";
        }
        var index = (int)Convert.ToInt64(NarrowNum(expression, Vb6Value.ValueType.Integer).Value);
        if (index is < 1 or > 255)
            throw InvalidCall();
        var all = Variables();
        return index <= all.Count ? all[index - 1].Name + "=" + all[index - 1].Value : "";
    }

    private static List<(string Name, string Value)> Variables() =>
        [.. Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .Select(e => (Name: (string)e.Key, Value: e.Value as string ?? ""))
            .Where(v => v.Name.Length > 0 && v.Name[0] != '=')
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Error(n) is n's text (VbErrorMessages). Measured: n is read as CLng reads it, so 65535.5 is 65536, and past 65535 it
    /// is 6, as past a Long; any negative number, True included, is "Application-defined or object-defined error". Null is
    /// 94, an array 13, and Empty is 0, whose text is "". Error with no number is the text of the error Err holds: its
    /// Description, or where it has none its number's, so after <c>Err.Number = 6</c> it is "Overflow".
    /// </summary>
    private static string ErrorText(VB6BuiltIns self, IReadOnlyList<Vb6Value> a)
    {
        if (!Supplied(a, 0))
        {
            var err = self.ErrObject?.Invoke();
            return err is null ? "" : err.Description.Length > 0 ? err.Description : VbErrorMessages.For(err.Number);
        }
        var number = CompareArgument(a[0]);
        if (number > 65535)
            throw new VBRunTimeException(VBStandardError.Overflow);
        return VbErrorMessages.For(number);
    }
}
