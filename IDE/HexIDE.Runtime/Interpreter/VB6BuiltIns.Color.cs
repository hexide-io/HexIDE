using System;
using System.Collections.Generic;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Interpreter;

// RGB and QBColor. Every value, type and error below was measured on vb6.exe; the probes are in
// docs/vb6-fidelity-oracle/rgb-qbcolor-and-msgboxs-style.md, "RGB, QBColor and MsgBox's style".
public partial class VB6BuiltIns
{
    // QBColor's sixteen, as the Long it returns: blue in the high byte, so QBColor(1), blue, is &H800000.
    private static readonly long[] QBColors =
    [
        0, 8388608, 32768, 8421376, 128, 8388736, 32896, 12632256,
        8421504, 16711680, 65280, 16776960, 255, 16711935, 65535, 16777215,
    ];

    private static void RegisterColor(Dictionary<string, BuiltinFn> d)
    {
        // Each argument is an Integer, coerced as CInt does: banker's rounding (RGB(1.5, 2.5, 0) is 514), a numeric
        // string read as a number, Null 94, a non-numeric string 13, and anything outside Integer's range 6
        // (RGB(40000, 0, 0)). Only then is a negative component 5, and one above 255 is 255. A Long, not an Integer:
        // TypeName(RGB(1, 0, 0)) is "Long", and new Vb6Value(int) would make it an Integer by magnitude.
        //
        // All three are coerced before any is range-checked, last first. Measured: RGB(-1, 40000, 0) is 6, not 5;
        // RGB(0, Null, "x") is 13 and RGB(0, "x", Null) 94, so the third goes before the second; and
        // RGB(Null, "x", 0) is 13, so the second goes before the first.
        d["RGB"] = (_, a, _) =>
        {
            var blue = ColorComponent(a[2]);
            var green = ColorComponent(a[1]);
            var red = ColorComponent(a[0]);
            if (red < 0 || green < 0 || blue < 0)
                throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
            return new Vb6Value(Math.Min(red, 255) + 256L * Math.Min(green, 255) + 65536L * Math.Min(blue, 255));
        };

        // QBColor(n) for n in 0 to 15, coerced as CInt does (7.5 is 8, 40000 is 6), and 5 outside it. A Long, as RGB.
        d["QBColor"] = (_, a, _) =>
        {
            var n = Convert.ToInt32(NarrowNum(a[0], VT.Integer).Value);
            if (n is < 0 or > 15) throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
            return new Vb6Value(QBColors[n]);
        };
    }

    private static long ColorComponent(Vb6Value v) => Convert.ToInt32(NarrowNum(v, VT.Integer).Value);
}
