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
        d["CStr"]  = (_, a, _) => a[0].IsNull ? throw InvalidUseOfNull()
            : new Vb6Value(a[0].Type == VT.Error
                ? "Error " + ((ushort)a[0].Value!).ToString(CultureInfo.InvariantCulture) : AsStr(a[0]));   // no leading space
        d["CDate"] = (_, a, _) => CDate(a[0]);
        // CDate under its older name, and the same answer (measured: CVDate("36526") is 01/01/2000, CVDate("1.5") 01:05).
        d["CVDate"] = (_, a, _) => a[0].Type == VT.Error ? throw UnmeasuredErrorConversion() : CDate(a[0]);
        // Its argument as a Variant, keeping the argument's own type: Integer for 5, Null for Null, the array or the object
        // itself (measured, #1466). ExpressionExecutor reads the result as a Variant's, so CVar(5) * 7000 widens to a Long.
        d["CVar"]  = (_, a, _) => a[0];
        d["CVErr"] = (_, a, _) =>
        {
            var number = (long)NarrowNum(a[0], VT.Long).Value!;
            if (number is < 0 or > 65535) throw InvalidCall();
            return Vb6Value.NewError((ushort)number).AsVariant();
        };
        d["Hex"]   = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(RadixStr(a[0], 16));
        d["Oct"]   = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(RadixStr(a[0], 8));
    }

    private static NotImplementedException UnmeasuredErrorConversion() =>
        new("This conversion of an Error Variant is unmeasured (#2520)");

    private static VBRunTimeException InvalidUseOfNull() => new(VBStandardError.InvalidUseOfNull);

    // Coerce (parsing strings, mapping bools/dates) then narrow to the target numeric subtype.
    internal static Vb6Value NarrowNum(Vb6Value v, VT target)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        // An omitted Optional Variant is the error value VB6 passes for a missing argument, whose number is 448. Measured:
        // CLng and CInt of one are both 448, with no error. It read as 0.
        if (v.IsMissing)
            v = new Vb6Value(MissingArgumentNumber);
        if (v.Type == VT.Error)
        {
            if (target == VT.Decimal)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            v = new Vb6Value((long)(ushort)v.Value!);
        }
        v = RefuseNonScalar(v);
        if (v.Type == VT.String && (target == VT.Currency || target == VT.Decimal))
        {
            // Preserve decimal digits and exact half-to-even rounding (measured, #1531).
            // Other accepted forms (hex, octal, exponents) retain the Double reader.
            try
            {
                var number = decimal.TryParse(WithoutThousandsAfterDigit(AsStr(v).Trim()), NumberStyle & ~NumberStyles.AllowExponent,
                    VbLocale.Current, out var parsed)
                    ? Vb6Value.NewDecimal(parsed)
                    : new Vb6Value(ToNum(v));
                return VbNumeric.Narrow(number, target, null);
            }
            catch (OverflowException) { throw new VBRunTimeException(VBStandardError.Overflow); }
        }
        var num = v.Type == VT.String || v.Type == VT.Boolean ? new Vb6Value(ToNum(v, target, allowCurrency: target == VT.Long)) : v;
        return VbNumeric.Narrow(num, target, null);
    }

    /// <summary>
    /// Raises for an argument that is not a number, a string or a date, which <see cref="VbNumeric.Narrow"/> would
    /// otherwise read as 0. Measured on vb6.exe, through CInt, CLng, RGB, QBColor and MsgBox's style alike.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>Nothing</c> is 91. A literal <c>Nothing</c> is a compile error, "Invalid use of object", so this is
    /// the variable's case.</item>
    /// <item>An array is 13. A fixed array named directly is a compile-time "Type mismatch"; one in a Variant raises
    /// it at run time.</item>
    /// <item>A user-defined type does not compile (measured, "Type mismatch"), so it is 13 here, at run time.</item>
    /// <item>A class instance with no default member is 438.</item>
    /// <item>A class whose member carries <c>Attribute X.VB_UserMemId = 0</c>, or whose Public variable carries
    /// <c>VB_VarUserMemId = 0</c>, stands for that member: measured, <c>CInt(k)</c> is 42 where the member returns 42,
    /// including when the attribute sits in the Property Let rather than the Get, and 7 for a variable holding 7.
    /// HexIDE does not call a class's default member yet (#174),
    /// so it refuses, untrappably. A trappable 438 there would let <c>On Error Resume Next</c> carry on without the
    /// value VB6 gives.</item>
    /// <item>A control stands for its default member: <c>CInt(Text1)</c> is <c>CInt(Text1.Text)</c>. HexIDE has no
    /// default members for controls yet (#790), so it refuses, untrappably, as a Let of one does.</item>
    /// </list>
    /// </remarks>
    /// <returns>The value to convert: <paramref name="v"/> itself, or what an in-box object stands for (Err's Number).
    /// A caller converts this, never the argument it passed, or Err would reach the conversion as an object.</returns>
    internal static Vb6Value RefuseNonScalar(Vb6Value v)
    {
        // An element of an array of a user-defined type that nothing has assigned holds no VbUdt yet, so the type
        // decides, not the value.
        if (v.Type.IsArray || v.Type == VT.UserDefinedType || v.Value is VbUdt)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (v.Type != VT.Object && v.Type != VT.Control && v.Type != VT.CSharpProxyObject)
            return v;
        if (v.Value is null)
            throw new VBRunTimeException(VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
        v = ObjectAsValue.Scalar(v, null);
        if (v.Type != VT.Object && v.Type != VT.Control && v.Type != VT.CSharpProxyObject)
            return v;
        if (v.Type == VT.Object)
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        throw new NotImplementedException(
            "Converting a control or object to a number reads its default member, which is not implemented (#790). "
            + "Name the property instead.");
    }

    /// <summary>The number of the error value VB6 passes for an omitted argument (DISP_E_PARAMNOTFOUND): 448, "Named argument not found".</summary>
    private const long MissingArgumentNumber = 448;

    internal static double ToNum(Vb6Value v, VT? target = null, bool allowCurrency = false)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        if (v.Value is bool bo) return bo ? -1 : 0;
        if (v.Type == VT.String)
        {
            var s = AsStr(v).Trim();
            // Hex/octal Strings use the value's width; a literal's trailing & is not accepted here (#788).
            if (s.Length > 2 && s[0] == '&' && (s[1] == 'H' || s[1] == 'h' || s[1] == 'O' || s[1] == 'o'))
            {
                var digits = s.Substring(2);
                try
                {
                    return RadixNumber(Convert.ToInt64(digits, s[1] is 'H' or 'h' ? 16 : 8), target);
                }
                catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
                {
                    throw new VBRunTimeException(VBStandardError.TypeMismatch);
                }
            }
            // In the program's locale, as CStr writes, so CDbl(CStr(x)) round-trips in any locale (#777). Measured on
            // vb6.exe under en-GB and de-DE (#770, #1047), CDbl and an arithmetic operand alike:
            // - the locale's thousands separator is skipped wherever it falls: "1,000" and "6,5" are 1000 and 65 under
            //   en-GB, and "1,000" is 1 and "6.5" is 65 under de-DE;
            // - a trailing sign and parentheses make a negative: "6-" and "(6)" are both -6;
            // - D marks an exponent as E does: "1D3" is 1000.
            // The UpDown buddy probe also measures "$3" as Long 3 on en-US (#1093).
            s = WithoutThousandsAfterDigit(s);
            var style = NumberStyle | (allowCurrency ? NumberStyles.AllowCurrencySymbol : 0);
            if (!double.TryParse(s, style, VbLocale.Current, out var sd)
                && !(s.AsSpan().IndexOfAny('d', 'D') >= 0
                     && double.TryParse(s.Replace('d', 'e').Replace('D', 'E'), style, VbLocale.Current, out sd)))
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            return Finite(sd, s);
        }
        if (Vb6Value.TryNumericToDouble(v, out var d)) return d;   // numeric + Date (via OA serial)
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    // SP6 native and p-code (#788): CInt/Val sign-extend 16 bits only while V fits;
    // above that use 32 bits, retaining the low bits for CInt/CByte/Val beyond a Long.
    private static double RadixNumber(long value, VT? target)
    {
        if (value >= 0 && value <= ushort.MaxValue && target == VT.Integer)
            return unchecked((short)value);
        if (value >= 0 && (value <= uint.MaxValue || target == VT.Integer || target == VT.Byte))
            return unchecked((int)value);
        return value;
    }

    // SP6 en-GB, native and p-code (#1446): separators after a digit disappear even after the point.
    // Keep any separator before the first digit so the parser still refuses a leading one.
    // Shared by the decimal-exact reader and IsNumeric, never by Val's invariant prefix scanner.
    private static string WithoutThousandsAfterDigit(string text)
    {
        var separator = VbLocale.Current.NumberFormat.NumberGroupSeparator;
        if (separator.Length == 0) return text;
        var firstDigit = text.AsSpan().IndexOfAnyInRange('0', '9');
        if (firstDigit < 0) return text;
        return text[..(firstDigit + 1)] + text[(firstDigit + 1)..].Replace(separator, "", StringComparison.Ordinal);
    }

    private const NumberStyles NumberStyle = NumberStyles.Float | NumberStyles.AllowThousands
                                             | NumberStyles.AllowTrailingSign | NumberStyles.AllowParentheses;

    /// <summary>
    /// A number .NET parsed from <paramref name="text"/>, or the error VB6 raises for it. .NET reads "NaN",
    /// "Infinity" and "-Infinity" as numbers; VB6 has neither, and CDbl of each is 13 (measured). Digits too large
    /// for a Double are Infinity to .NET as well, and that one is Overflow: CDbl("1E+400") is 6 (measured).
    /// </summary>
    private static double Finite(double parsed, string text)
    {
        if (double.IsNaN(parsed))
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (double.IsInfinity(parsed))
            throw new VBRunTimeException(text.AsSpan().ContainsAnyInRange('0', '9')
                ? VBStandardError.Overflow
                : VBStandardError.TypeMismatch);
        return parsed;
    }

    internal static Vb6Value CBool(Vb6Value v)
    {
        if (v.Type == VT.Error) return new Vb6Value((ushort)v.Value! != 0);
        if (v.IsNull) throw InvalidUseOfNull();
        // An object, a control, an in-box object or Nothing, as for every other conversion. CBool was the one that never
        // asked, so each fell through ToNum to a trappable 13 (#847's review).
        v = RefuseNonScalar(v);
        if (v.Value is bool b) return new Vb6Value(b);
        // Measured: CBool(Empty) is False, as Empty is 0 to CLng and CDbl. `Enabled = Empty` takes this path.
        if (v.Type == VT.EmptyVariant) return new Vb6Value(false);
        if (v.Type == VT.String)
        {
            var s = AsStr(v).Trim();
            if (bool.TryParse(s, out var bb)) return new Vb6Value(bb);
            // Otherwise the number CDbl reads, by the same parse and in the same locale (measured): "NaN" and
            // "Infinity" are 13, where `Enabled = "NaN"` used to be True; "1E+400" is 6; and "&H10" is True, which
            // this used to refuse.
            return new Vb6Value(ToNum(v) != 0);
        }
        return new Vb6Value(ToNum(v) != 0);
    }

    internal static Vb6Value CDate(Vb6Value v)
    {
        if (v.Type == VT.Error) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (v.IsNull) throw InvalidUseOfNull();
        if (v.Value is VbDate) return v;
        if (v.Type == VT.String)
            // In the program's locale: `CDate("1/2/2003")` is 1 February under en-GB, and CDate(CStr(d)) is d (#777). A
            // serial is read too: `CDate("36526")` is 1 January 2000 (#967).
            return StringToDate(AsStr(v), null);
        // A serial outside the Date range, strictly between -657435 and 2958466, is a trappable Overflow (Err 6,
        // oracle-verified). Inside it the serial is kept exactly, a fraction of a second included (measured, #783).
        var serial = ToNum(v);
        return VbDate.InRange(serial)
            ? new Vb6Value(new VbDate(serial))
            : throw new VBRunTimeException(VBStandardError.Overflow);
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
