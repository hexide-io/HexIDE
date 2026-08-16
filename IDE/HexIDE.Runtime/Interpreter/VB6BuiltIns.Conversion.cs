using System;
using System.Globalization;
using System.Collections.Generic;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Interpreter;

// Phase 3 — Conversion intrinsics. The numeric conversions delegate to VbNumeric.Narrow (banker's rounding +
// range-check -> Overflow, Err 6). Every rule here was verified against vb6.exe (Hex/Oct width, CStr/CBool/CDate).
public partial class VB6BuiltIns
{
    private static void RegisterConversion(Dictionary<string, BuiltinFn> d)
    {
        d["CByte"] = (_, a, _) => NarrowNum(a[0], VT.Byte);
        d["CInt"]  = (_, a, _) => NarrowNum(a[0], VT.Integer);
        d["CLng"]  = (_, a, _) => NarrowNum(a[0], VT.Long);
        d["CSng"]  = (_, a, _) => NarrowNum(a[0], VT.Single);
        d["CDbl"]  = (_, a, _) => NarrowNum(a[0], VT.Double);
        d["CCur"]  = (_, a, _) => NarrowNum(a[0], VT.Currency);
        d["CDec"]  = (_, a, _) => NarrowNum(a[0], VT.Decimal);
        d["CBool"] = (_, a, _) => CBool(a[0]);
        d["CStr"]  = (_, a, _) => a[0].IsNull ? throw InvalidUseOfNull() : new Vb6Value(AsStr(a[0]));   // no leading space
        d["CDate"] = (_, a, _) => CDate(a[0]);
        d["Hex"]   = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(RadixStr(a[0], 16));
        d["Oct"]   = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(RadixStr(a[0], 8));
    }

    private static VBRunTimeException InvalidUseOfNull() => new(VBStandardError.InvalidUseOfNull);

    // Coerce (parsing strings, mapping bools/dates) then narrow to the target numeric subtype.
    private static Vb6Value NarrowNum(Vb6Value v, VT target)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        var num = v.Type == VT.String || v.Type == VT.Boolean ? new Vb6Value(ToNum(v)) : v;
        return VbNumeric.Narrow(num, target, null);
    }

    private static double ToNum(Vb6Value v)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        if (v.Value is bool bo) return bo ? -1 : 0;
        if (v.Type == VT.String)
            return double.TryParse(AsStr(v).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sd)
                ? sd : throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (Vb6Value.TryNumericToDouble(v, out var d)) return d;   // numeric + Date (via OA serial)
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    private static Vb6Value CBool(Vb6Value v)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        if (v.Value is bool b) return new Vb6Value(b);
        if (v.Type == VT.String)
        {
            var s = AsStr(v).Trim();
            if (bool.TryParse(s, out var bb)) return new Vb6Value(bb);
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sd)) return new Vb6Value(sd != 0);
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        }
        return new Vb6Value(ToNum(v) != 0);
    }

    private static Vb6Value CDate(Vb6Value v)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        if (v.Value is DateTime) return v;
        if (v.Type == VT.String)
            return DateTime.TryParse(AsStr(v).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? new Vb6Value(dt) : throw new VBRunTimeException(VBStandardError.TypeMismatch);
        // A serial outside the Date range (about -657434 .. 2958465) is a trappable Overflow (Err 6, oracle-verified);
        // FromOADate would otherwise throw an UNCATCHABLE ArgumentException (AsDate wraps this too, at DateTime.cs).
        try { return new Vb6Value(DateTime.FromOADate(ToNum(v))); }
        catch (ArgumentException) { throw new VBRunTimeException(VBStandardError.Overflow); }
    }

    // VB6 Hex/Oct: the WIDTH follows the operand's type (verified vs vb6.exe) — Byte/Integer -> 16-bit,
    // Long (or a larger value) -> 32-bit — and negatives are the two's-complement pattern at that width.
    private static string RadixStr(Vb6Value v, int radix)
    {
        if (v.Type == VT.Byte || v.Type == VT.Integer)
            return Width16((short)AsInt(v), radix);
        long l = (long)Math.Round(AsDouble(v), MidpointRounding.ToEven);
        if (v.Type != VT.Long && l >= short.MinValue && l <= short.MaxValue)
            return Width16((short)l, radix);
        uint u32 = (uint)(int)l;
        return radix == 16 ? u32.ToString("X") : Convert.ToString(u32, 8);
    }

    private static string Width16(short s, int radix)
    {
        ushort u = (ushort)s;
        return radix == 16 ? u.ToString("X") : Convert.ToString(u, 8);
    }
}
