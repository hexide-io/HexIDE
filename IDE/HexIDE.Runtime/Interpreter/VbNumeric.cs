using System;
using System.Numerics;
using Antlr4.Runtime;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The single home for VB6 numeric semantics: the per-operator arithmetic result-type tables, checked narrowing
/// (out-of-range -> Overflow, Err 6), and the integer <c>\</c>/<c>Mod</c> operators (operands banker's-rounded to an
/// integer first; divide-by-zero -> Err 11). The rules here were verified against real <c>vb6.exe</c>.
/// </summary>
public static class VbNumeric
{
    // Oracle: "Numeric type pairs — five arithmetic tables" (2026-09-03).
    // Rows/columns: Byte, Integer, Long, Single, Double, Currency, Decimal.
    // B/I/L/S/F/C/D are those result types. + and - share AddTypes; backslash and Mod share IntegerTypes.
    private static readonly string[] AddTypes =
    [
        "BILSFCD", // Byte
        "IILSFCD", // Integer
        "LLLFFCD", // Long
        "SSFSFCD", // Single
        "FFFFFCD", // Double
        "CCCCCCD", // Currency
        "DDDDDDD", // Decimal
    ];
    private static readonly string[] MultiplyTypes =
    [
        "BILSFCD", // Byte
        "IILSFCD", // Integer
        "LLLFFCD", // Long
        "SSFSFFD", // Single
        "FFFFFFD", // Double
        "CCCFFCD", // Currency
        "DDDDDDD", // Decimal
    ];
    private static readonly string[] DivideTypes =
    [
        "FFFSFFD", // Byte
        "FFFSFFD", // Integer
        "FFFFFFD", // Long
        "SSFSFFD", // Single
        "FFFFFFD", // Double
        "FFFFFFD", // Currency
        "DDDDDDD", // Decimal
    ];
    private static readonly string[] IntegerTypes =
    [
        "BILLLLL", // Byte
        "IILLLLL", // Integer
        "LLLLLLL", // Long
        "LLLLLLL", // Single
        "LLLLLLL", // Double
        "LLLLLLL", // Currency
        "LLLLLLL", // Decimal
    ];
    // The fifth table, ^, is uniformly Double and is encoded directly in Power.

    private static VT ResultType(string[] table, int left, int right) =>
        CoreType("BILSFCD".IndexOf(table[left][right]));

    // The tables' row and column index; not a promotion ladder. Boolean and Empty index as Integer. Date has no
    // index: Add/Sub special-case it (Date+n -> Date, Date-Date -> Double), and * / \ Mod ^ coerce a Date operand
    // to its Double serial through the fallback path. Also used by the unary and comparison helpers.
    private static int CoreRank(VT t)
    {
        if (t == VT.Byte) return 0;
        if (t == VT.Integer || t == VT.Boolean || t == VT.EmptyVariant) return 1;
        if (t == VT.Long) return 2;
        if (t == VT.Single) return 3;
        if (t == VT.Double) return 4;
        if (t == VT.Currency) return 5;
        if (t == VT.Decimal) return 6;
        return -1;   // Date/String/... : off the ladder
    }

    private static VT CoreType(int rank) => rank switch
    {
        0 => VT.Byte,
        1 => VT.Integer,
        2 => VT.Long,
        3 => VT.Single,
        4 => VT.Double,
        5 => VT.Currency,
        _ => VT.Decimal
    };

    /// <summary>
    /// A String operand, read as VB6 reads it: as the Double <c>CDbl</c> would make of it, or Type mismatch (#1047). Measured
    /// across every operator and partner, native, p-code and the IDE: <c>"6" * 2</c> is 12, and the String takes a Double's
    /// place in the result table, so <c>"6" + CCur(2)</c> is Currency, <c>"6" * CDec(2)</c> Decimal, and <c>"7" \ "2"</c> Long.
    /// </summary>
    private static Vb6Value ReadString(Vb6Value v, ParserRuleContext? ctx)
    {
        if (v.Type != VT.String)
            return v;
        try
        {
            return new Vb6Value(VB6BuiltIns.ToNum(v));
        }
        catch (VBRunTimeException e) when (ctx is not null)
        {
            throw new VBRunTimeException(ctx, e.Error);
        }
    }

    // ===== + - * =====

    public static Vb6Value Add(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        l = ReadString(l, ctx);
        r = ReadString(r, ctx);
        if (l.Type == VT.Date || r.Type == VT.Date)
            return FromSerial(SerialOf(l) + SerialOf(r), ctx);   // Date + anything -> Date (adds serial days)
        return Arith(l, r, ctx, '+');
    }

    public static Vb6Value Sub(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        l = ReadString(l, ctx);
        r = ReadString(r, ctx);
        bool ld = l.Type == VT.Date, rd = r.Type == VT.Date;
        if (ld && rd)
            return new Vb6Value(SerialOf(l) - SerialOf(r));      // Date - Date -> Double (day difference)
        if (ld || rd)
            return FromSerial(SerialOf(l) - SerialOf(r), ctx);   // Date - n (or n - Date) -> Date
        return Arith(l, r, ctx, '-');
    }

    public static Vb6Value Mul(Vb6Value l, Vb6Value r, ParserRuleContext? ctx) =>
        Arith(ReadString(l, ctx), ReadString(r, ctx), ctx, '*');

    private static Vb6Value Arith(Vb6Value l, Vb6Value r, ParserRuleContext? ctx, char op)
    {
        int lr = CoreRank(l.Type), rr = CoreRank(r.Type);
        if (lr < 0 || rr < 0)
        {
            // A Date operand in * (or a stray off-ladder type) coerces to its Double serial.
            if (!TryToDouble(l, out var da) || !TryToDouble(r, out var db))
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
            return new Vb6Value(op == '+' ? da + db : op == '-' ? da - db : da * db);
        }

        var rt = ResultType(op == '*' ? MultiplyTypes : AddTypes, lr, rr);
        if (rt == VT.Currency || rt == VT.Decimal)
        {
            // In + and -, a Single or Double beside a Currency becomes a Currency first, by its exact binary value, and
            // the sum is taken in Currency: 2@ + 0.00005 is 2.0001, where a sum taken in Double would round to 2, and
            // 1@ + 0.00005! is 1 where 1@ + 0.00005 is 1.0001 (measured, #966, #230). The * table makes a Currency times
            // a Single or Double a Double, so * never reaches here with one.
            decimal res;
            try
            {
                decimal a = rt == VT.Currency ? CurrencyOperand(l, ctx) : ToDecimal(l);
                decimal b = rt == VT.Currency ? CurrencyOperand(r, ctx) : ToDecimal(r);
                res = op == '+' ? a + b : op == '-' ? a - b : a * b;
            }
            catch (OverflowException) { throw new VBRunTimeException(ctx, VBStandardError.Overflow); }
            return rt == VT.Currency ? MakeCurrency(res, ctx) : Vb6Value.NewDecimal(res);
        }
        if (rt == VT.Single || rt == VT.Double)
        {
            double a = ToDouble(l), b = ToDouble(r);
            return NarrowDouble(op == '+' ? a + b : op == '-' ? a - b : a * b, rt, ctx, l.HasFixedType && r.HasFixedType);
        }

        long la = ToLong(l), lb = ToLong(r);
        return NarrowLong(op == '+' ? la + lb : op == '-' ? la - lb : la * lb, rt, ctx, l.HasFixedType && r.HasFixedType);
    }

    // ===== / (real division) =====

    public static Vb6Value RealDivide(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        l = ReadString(l, ctx);
        r = ReadString(r, ctx);
        if (!TryToDouble(l, out var a) || !TryToDouble(r, out var b))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
        // Zero over zero is Overflow, and anything else over zero is Division by zero. A Decimal operand on either side
        // makes it Decimal's division, which says Division by zero for both (measured, #234: every core type, Boolean,
        // Empty, String and mixed operands, native, p-code and the IDE alike).
        if (b == 0.0)
            throw new VBRunTimeException(ctx, a == 0.0 && l.Type != VT.Decimal && r.Type != VT.Decimal
                ? VBStandardError.Overflow
                : VBStandardError.DivisionByZero);
        int lr = CoreRank(l.Type), rr = CoreRank(r.Type);
        var rt = lr >= 0 && rr >= 0 ? ResultType(DivideTypes, lr, rr) : VT.Double;
        if (rt == VT.Decimal)
        {
            try { return Vb6Value.NewDecimal(ToDecimal(l) / ToDecimal(r)); }
            catch (DivideByZeroException) { throw new VBRunTimeException(ctx, VBStandardError.DivisionByZero); }
            catch (OverflowException) { throw new VBRunTimeException(ctx, VBStandardError.Overflow); }
        }
        // Through NarrowDouble on BOTH paths. The Single path always went through it; the Double path did
        // not, so `1E+308 / 0.0001` handed back an infinity instead of the Overflow vb6.exe raises.
        double res = a / b;
        return NarrowDouble(res, rt, ctx, l.HasFixedType && r.HasFixedType);
    }

    // ===== \ (integer division) and Mod =====

    public static Vb6Value IntDivide(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        RefuseBooleanOverString(l, r, "\\", ctx);
        l = ReadString(l, ctx);
        r = ReadString(r, ctx);
        long a = RoundToLong(l, ctx), b = RoundToLong(r, ctx);
        if (b == 0)
            throw new VBRunTimeException(ctx, VBStandardError.DivisionByZero);
        return NarrowLong(a / b, IntResultType(l.Type, r.Type), ctx);   // C# / truncates toward zero, as VB6 \
    }

    public static Vb6Value Modulo(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        RefuseBooleanOverString(l, r, "Mod", ctx);
        l = ReadString(l, ctx);
        r = ReadString(r, ctx);
        long a = RoundToLong(l, ctx), b = RoundToLong(r, ctx);
        if (b == 0)
            throw new VBRunTimeException(ctx, VBStandardError.DivisionByZero);
        return NarrowLong(a % b, IntResultType(l.Type, r.Type), ctx);   // C# % sign follows dividend, as VB6 Mod
    }

    // A Boolean divided by a String, with \ or Mod, is the one shape where VB6 does not read the String as a Double:
    // `True \ "6"` is the Boolean True and `True Mod "6"` the Boolean False (measured, native and p-code), though
    // `"6" \ True` is the Long -6. Read as a Double it would come out a Long 0, a wrong value, so it is refused (#1047).
    private static void RefuseBooleanOverString(Vb6Value l, Vb6Value r, string op, ParserRuleContext? ctx)
    {
        if (l.Type == VT.Boolean && r.Type == VT.String)
            throw new NotImplementedException(
                $"A Boolean {op} a String is not implemented: VB6 gives a Boolean there, by a rule not yet measured (#1047).");
    }

    // ===== ^ and unary minus =====

    public static Vb6Value Power(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        l = ReadString(l, ctx);
        r = ReadString(r, ctx);
        if (!TryToDouble(l, out var a) || !TryToDouble(r, out var b))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
        // `^` distinguishes a DOMAIN error from an overflow, and the two get different numbers (measured):
        // 0 to a negative power is Err 5, not the Err 6 its infinite result would otherwise suggest, and a
        // negative base to a fractional power — .NET's NaN — is Err 5 as well. Only a genuine magnitude
        // overflow (1E+308 ^ 2) is Err 6. NarrowDouble maps NaN → 5 and Infinity → 6, so the one case it
        // cannot infer is 0 ^ negative, whose result is infinite but whose cause is not magnitude.
        if (a == 0 && b < 0)
            throw new VBRunTimeException(ctx, VBStandardError.InvalidProcedureCall);
        return NarrowDouble(Math.Pow(a, b), VT.Double, ctx);   // ^ is always Double
    }

    public static Vb6Value Negate(Vb6Value v, ParserRuleContext? ctx)
    {
        v = ReadString(v, ctx);
        int rank = CoreRank(v.Type);
        if (rank < 0)
        {
            if (!TryToDouble(v, out var d))
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
            return new Vb6Value(-d);
        }
        if (v.Type == VT.Currency)
            return MakeCurrency(-ToDecimal(v), ctx);   // -1@ remains Currency (#1073)
        var rt = rank <= 1 ? VT.Integer : CoreType(rank);   // Byte/Boolean negate into Integer
        if (rt == VT.Integer || rt == VT.Long)
            return NarrowLong(-ToLong(v), rt, ctx, v.HasFixedType);
        return NarrowDouble(-ToDouble(v), rt, ctx);
    }

    // ===== narrowing (coercion-on-store, conversions) =====

    /// <summary>Coerce a value to <paramref name="target"/>: widening is lossless; narrowing rounds half-to-even
    /// and range-checks (out of range -> Overflow). A non-core-numeric target is left unchanged (a later phase).</summary>
    public static Vb6Value Narrow(Vb6Value v, VT target, ParserRuleContext? ctx)
    {
        if (v.Type == target)
            return v;
        if (target == VT.Byte || target == VT.Integer || target == VT.Long)
        {
            long r = (long)Math.Round(ToDouble(v), MidpointRounding.ToEven);
            return NarrowLong(r, target, ctx);
        }
        if (target == VT.Single)
            return NarrowDouble(ToDouble(v), VT.Single, ctx);
        if (target == VT.Double)
            return new Vb6Value(ToDouble(v));
        if (target == VT.Currency)
            return v.Type == VT.Double
                ? CurrencyFromDouble((double)v.Value!, ctx)
                : MakeCurrency(ToDecimal(v), ctx);
        if (target == VT.Decimal)
            return Vb6Value.NewDecimal(ToDecimal(v));
        if (target == VT.Date)
            return FromSerial(ToDouble(v), ctx);
        return v;
    }

    /// <summary>
    /// Coerce a value on its way into a <b>declared</b> variable, which is what makes `Dim l As Long` mean
    /// anything after the Dim line. VB6 stores the declared type, not the assigned value's — so `l = 3` holds
    /// a Long 3 and `TypeName(l)` is "Long", where a Variant would hold an Integer.
    ///
    /// <para>
    /// Every rule here is vb6.exe output (see <i>Coercion-on-store</i> in docs/vb6-fidelity-oracle/verified-findings.md):
    /// String takes anything (`5` → "5", `True` → "True"); Boolean is CBool (any non-zero is True, "banana"
    /// is Err 13); a numeric target parses a String and raises Err 13 on garbage, rounds half-to-even
    /// (`12.5` → 12, `12.6` → 13) and range-checks (`Dim b As Byte : b = 300` is Err 6); Empty is 0 or "";
    /// and <b>Null into any declared variable is Err 94</b>, including a String one.
    /// </para>
    ///
    /// <para>
    /// A Variant target never gets here — the caller only calls this for slots that recorded a declared
    /// scalar type, which is the whole distinction VB6 draws and HexIDE previously could not.
    /// </para>
    /// </summary>
    public static Vb6Value CoerceOnStore(Vb6Value v, VT target, ParserRuleContext? ctx)
    {
        if (v.Type == VT.Error && (target == VT.Long || target == VT.String))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
        if (v.Type == VT.Error)
            throw new NotImplementedException("Assignment of an Error Variant to this declared type is unmeasured (#2520)");
        if (v.Type == VT.AutomationUnsignedLong)
            throw new NotImplementedException("Conversion of an unsigned Automation Long is not implemented (#1084)");
        if (v.Type == target)
            return v;
        // A declared scalar slot takes an object's default member (ObjectAsValue).
        v = ObjectAsValue.Scalar(v, ctx);
        if (v.Type == target)
            return v;
        // Before every branch, including String: `Dim s As String : s = Null` is Err 94, not "".
        if (v.IsNull)
            throw new VBRunTimeException(ctx, VBStandardError.InvalidUseOfNull);
        if (target == VT.String)
            return new Vb6Value(VbDisplay.ToText(v));
        if (target == VT.Boolean)
            return VB6BuiltIns.CBool(v);
        // A String into a Date is read as CDate reads it, a date first and then a serial (measured, #967). ToNum read the
        // serial alone, so `d = "1/2/2003"` was 13, and `d = "1,5"` the 15th day rather than 1 May.
        if (target == VT.Date && v.Type == VT.String)
            return VB6BuiltIns.StringToDate((string)v.Value!, ctx);
        // Narrow's ToDouble answers 0.0 for a String rather than parsing it, so `Dim i As Integer : i = "12"`
        // would silently store 0 and `i = "abc"` would too. ToNum is the interpreter's own already-pinned
        // string→number rule, Err 13 and all. Routed only for Strings, so the Currency/Decimal paths below
        // keep their decimal precision instead of going through a double.
        if (v.Type == VT.String)
            return Narrow(new Vb6Value(VB6BuiltIns.ToNum(v, allowCurrency: target == VT.Long)), target, ctx);
        return Narrow(v, target, ctx);
    }

    /// <summary>True if <paramref name="t"/> is a subtype a DECLARED variable can hold, and therefore one
    /// <see cref="CoerceOnStore"/> can target. Excludes Variant/Empty (no declared type at all), Object,
    /// UDT and arrays, whose slots are replaced wholesale rather than coerced.</summary>
    public static bool IsDeclarableScalar(VT t) =>
        !t.IsArray && (IsCoreNumeric(t) || t == VT.Decimal || t == VT.Date || t == VT.String || t == VT.Boolean);

    /// <summary>True if <paramref name="t"/> is a fixed numeric subtype that coercion-on-store should target
    /// (i.e. a declared numeric variable, not a Variant/Empty/String/Object).</summary>
    public static bool IsCoreNumeric(VT t) =>
        t == VT.Byte || t == VT.Integer || t == VT.Long || t == VT.Single || t == VT.Double || t == VT.Currency;

    // ===== Currency / Date helpers =====

    // Currency is a fixed 4 dp (banker's) with a hardware range; a result outside it is Overflow (Err 6).
    private static Vb6Value MakeCurrency(decimal value, ParserRuleContext? ctx)
    {
        var r = Math.Round(value, 4, MidpointRounding.ToEven);
        if (r < -922_337_203_685_477.5808m || r > 922_337_203_685_477.5807m)
            throw new VBRunTimeException(ctx, VBStandardError.Overflow);
        return Vb6Value.NewCurrency(r);
    }

    private static Vb6Value CurrencyFromDouble(double value, ParserRuleContext? ctx)
    {
        var (numerator, denominator) = DoubleFraction(value, ctx);
        return RoundCurrency(numerator, denominator, ctx);
    }

    // A Currency operand of + or -: a Single or Double by its exact binary value, anything else as it stands.
    private static decimal CurrencyOperand(Vb6Value value, ParserRuleContext? ctx) =>
        value.Type == VT.Single || value.Type == VT.Double
            ? (decimal)CurrencyFromDouble(ToDouble(value), ctx).Value!
            : ToDecimal(value);

    private static (BigInteger Numerator, BigInteger Denominator) DoubleFraction(double value, ParserRuleContext? ctx)
    {
        if (!double.IsFinite(value))
            throw new VBRunTimeException(ctx, VBStandardError.Overflow);
        // (decimal)d keeps only about 15 significant digits: it turns 1.00005 and 0.00015 into
        // decimal ties, losing which side their binary values occupy (#966). Decode d = mantissa * 2^exponent
        // instead. BigInteger shifts and products preserve every bit, including subnormal values.
        ulong bits = BitConverter.DoubleToUInt64Bits(value);
        int exponentBits = (int)((bits >> 52) & 0x7ff);
        BigInteger mantissa = bits & 0x000f_ffff_ffff_ffffUL;
        if (exponentBits != 0)
            mantissa += BigInteger.One << 52;
        int exponent = exponentBits == 0 ? -1074 : exponentBits - 1023 - 52;
        if ((bits >> 63) != 0)
            mantissa = -mantissa;
        return exponent >= 0 ? (mantissa << exponent, BigInteger.One) : (mantissa, BigInteger.One << -exponent);
    }

    private static Vb6Value RoundCurrency(BigInteger numerator, BigInteger denominator, ParserRuleContext? ctx)
    {
        // Scale the exact fraction to Currency units, then round once. Comparing twice the remainder
        // with the denominator detects a true tie; the quotient's parity implements ties to even.
        BigInteger units = BigInteger.DivRem(BigInteger.Abs(numerator) * 10000, denominator, out var remainder);
        int comparison = (remainder * 2).CompareTo(denominator);
        if (comparison > 0 || (comparison == 0 && !units.IsEven))
            units++;
        if (numerator.Sign < 0)
            units = -units;
        if (units < long.MinValue || units > long.MaxValue)
            throw new VBRunTimeException(ctx, VBStandardError.Overflow);
        return Vb6Value.NewCurrency((decimal)units / 10000m);
    }

    private static double SerialOf(Vb6Value v) => v.Value is VbDate dt ? dt.Serial : ToDouble(v);

    /// <summary>
    /// The order of two numbers, a Date by its serial: exactly where neither is a Single or a Double, and as Doubles otherwise.
    /// An <c>#If</c>'s comparison reads it (#1112). Anything that is not a number is Type mismatch.
    /// </summary>
    internal static int CompareNumbers(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        static bool Exact(Vb6Value v) => CoreRank(v.Type) >= 0 && v.Type != VT.Single && v.Type != VT.Double;
        if (Exact(l) && Exact(r))
            return ToDecimal(l).CompareTo(ToDecimal(r));
        if (!TryToSerial(l, out var a) || !TryToSerial(r, out var b))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
        return a.CompareTo(b);
    }

    private static bool TryToSerial(Vb6Value v, out double serial)
    {
        if (v.Value is VbDate dt)
        {
            serial = dt.Serial;
            return true;
        }
        return TryToDouble(v, out serial);
    }

    private static Vb6Value FromSerial(double serial, ParserRuleContext? ctx)
    {
        return VbDate.InRange(serial)
            ? new Vb6Value(new VbDate(serial))
            : throw new VBRunTimeException(ctx, VBStandardError.Overflow);
    }

    private static decimal ToDecimal(Vb6Value v) => v.Value switch
    {
        byte b => b,
        int i => i,
        long l => l,
        float f => (decimal)f,
        double d => (decimal)d,
        decimal m => m,
        bool bo => bo ? -1 : 0,
        _ => 0m   // EmptyVariant / null
    };

    // ===== helpers =====

    private static VT IntResultType(VT l, VT r)
    {
        int li = CoreRank(l), ri = CoreRank(r);
        return li >= 0 && ri >= 0 ? ResultType(IntegerTypes, li, ri) : VT.Long;
    }

    /// <summary>
    /// Land an integral result on its target type.
    ///
    /// <paramref name="fixedType"/> is the whole of #122: a DECLARED type is a ceiling, so overflowing it
    /// is Err 6; a Variant has none, so it WIDENS — Byte -> Integer -> Long -> Double. Measured, including
    /// that a Variant on either side lifts the ceiling and that fixedness propagates, so an all-fixed
    /// operation hands back a fixed result and `(a + 0) * 3` still raises Err 6 for a declared `a`.
    ///
    /// VB6's Long is 32-bit, so anything past Int32 becomes Double — which is why untyped `Fact(13)` is a
    /// Double. The C# long intermediate is wide enough for the products that reach here: both operands are
    /// at most a VB6 Long, so the product cannot exceed Int64.
    /// </summary>
    private static Vb6Value NarrowLong(long res, VT target, ParserRuleContext? ctx, bool fixedType = true)
    {
        Vb6Value Land(Vb6Value v) => fixedType ? v.AsFixedType() : v;

        if (target == VT.Byte)
        {
            if (res >= 0 && res <= 255) return Land(new Vb6Value((byte)res));
            if (fixedType) throw new VBRunTimeException(ctx, VBStandardError.Overflow);
            target = VT.Integer;   // widen and fall through
        }
        if (target == VT.Integer)
        {
            if (res >= short.MinValue && res <= short.MaxValue)
                return Land(new Vb6Value((int)res));   // in Int16 range, so the magnitude ctor keeps it Integer
            if (fixedType) throw new VBRunTimeException(ctx, VBStandardError.Overflow);
            target = VT.Long;
        }
        // Long
        if (res >= int.MinValue && res <= int.MaxValue) return Land(new Vb6Value((long)res));
        if (fixedType) throw new VBRunTimeException(ctx, VBStandardError.Overflow);
        return new Vb6Value((double)res);   // past VB6's 32-bit Long: a Variant becomes Double
    }

    /// <summary>
    /// Land a double on its target type, and refuse to hand back a value VB6 has no way to represent.
    ///
    /// <para>
    /// <b>VB6 has no Infinity and no NaN.</b> IEEE-754 produces them, VB6 raises instead — measured across
    /// <c>*</c>, <c>/</c>, <c>^</c> and <c>Exp</c>: an infinite result is <c>Err 6</c> (Overflow) and a NaN is
    /// <c>Err 5</c> (Invalid procedure call). Letting either escape is worse than an error, because it is a
    /// wrong ANSWER: <c>1E+308 * 10</c> came back as the string "∞", which then propagates through every
    /// comparison and every subsequent sum as something no VB6 program can test for or recover from.
    /// </para>
    ///
    /// <para>
    /// The Single branch already raised when a finite double did not fit a float. That guard carried
    /// <c>&amp;&amp; !double.IsInfinity(res)</c>, which deliberately let an ALREADY-infinite double through —
    /// exactly the case that needed raising. Both now raise, which is what vb6.exe does either way.
    /// </para>
    /// </summary>
    /// <para>
    /// A Single that overflows widens to a Double unless both operands were typed, as an integral result widens
    /// (<see cref="NarrowLong"/>). Measured, #234: a Variant Single on either side lifts the ceiling for <c>+</c>,
    /// <c>-</c>, <c>*</c> and <c>/</c>, and two typed Singles raise Err 6. <paramref name="fixedType"/> is that
    /// condition, and an all-typed Single result stays typed, as an integral one does.
    /// </para>
    private static Vb6Value NarrowDouble(double res, VT target, ParserRuleContext? ctx, bool fixedType = true)
    {
        if (double.IsNaN(res))
            throw new VBRunTimeException(ctx, VBStandardError.InvalidProcedureCall);
        if (double.IsInfinity(res))
            throw new VBRunTimeException(ctx, VBStandardError.Overflow);
        if (target == VT.Single)
        {
            var f = (float)res;
            if (float.IsInfinity(f))
                return fixedType ? throw new VBRunTimeException(ctx, VBStandardError.Overflow) : new Vb6Value(res);
            return fixedType ? new Vb6Value(f).AsFixedType() : new Vb6Value(f);
        }
        return new Vb6Value(res);
    }

    private static long ToLong(Vb6Value v) => v.Value switch
    {
        byte b => b,
        int i => i,
        long l => l,
        bool bo => bo ? -1 : 0,
        _ => (long)ToDouble(v)   // Single/Double truncate toward zero
    };

    private static double ToDouble(Vb6Value v)
    {
        if (v.Type == VT.EmptyVariant) return 0.0;
        if (v.Value is bool b) return b ? -1 : 0;
        return Vb6Value.TryNumericToDouble(v, out var d) ? d : 0.0;
    }

    private static bool TryToDouble(Vb6Value v, out double d)
    {
        if (v.Type == VT.EmptyVariant) { d = 0; return true; }
        if (v.Value is bool b) { d = b ? -1 : 0; return true; }
        return Vb6Value.TryNumericToDouble(v, out d);
    }

    private static long RoundToLong(Vb6Value v, ParserRuleContext? ctx)
    {
        if (v.Value is byte b) return b;
        if (v.Value is int i) return i;
        if (v.Value is long l) return l;
        if (v.Type == VT.EmptyVariant) return 0;
        if (v.Value is bool bo) return bo ? -1 : 0;
        if (Vb6Value.TryNumericToDouble(v, out var d))
            return (long)Math.Round(d, MidpointRounding.ToEven);
        throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
    }
}
