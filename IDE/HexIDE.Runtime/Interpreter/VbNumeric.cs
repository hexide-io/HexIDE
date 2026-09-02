using System;
using Antlr4.Runtime;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The single home for VB6 numeric semantics: the arithmetic result-type table, checked narrowing
/// (out-of-range -> Overflow, Err 6), and the integer <c>\</c>/<c>Mod</c> operators (operands banker's-rounded
/// to an integer first; divide-by-zero -> Err 11). The rules here were verified against real <c>vb6.exe</c>.
///
/// Phase-2.3 scope is the numeric core — Byte/Integer/Long/Single/Double (plus Boolean and Empty, which behave
/// as Integer/0). Currency/Decimal/Date are not reachable from source until 2.5; if they reach here they fall
/// back to a Double computation, which 2.5 will replace with their precise (4-dp / serial) semantics.
///
/// Verified VB6 facts encoded here: Byte+Byte -> Byte (overflows at 255); Boolean/Byte -> Integer;
/// Int+Int -> Int, Int+Long -> Long; <c>/</c> is Double except when the widest operand is Single;
/// <c>5.5 Mod 2</c> = 0 as Long; <c>7.6 \ 2</c> = 4 as Long; CInt rounds half-to-even.
/// </summary>
public static class VbNumeric
{
    // Arithmetic ladder rank. Boolean and Empty behave as Integer; Byte stays Byte (Byte+Byte -> Byte).
    // Verified against vb6.exe: Currency DOMINATES Double for + - * (10@ + 1.5 -> Currency), and Decimal is
    // the widest exact type. Date is NOT on this ladder — Add/Sub special-case it (Date+n -> Date,
    // Date-Date -> Double), and * / \ Mod ^ coerce a Date operand to its Double serial via the fallback path.
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

    // ===== + - * =====

    public static Vb6Value Add(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        if (l.Type == VT.Date || r.Type == VT.Date)
            return FromSerial(SerialOf(l) + SerialOf(r), ctx);   // Date + anything -> Date (adds serial days)
        return Arith(l, r, ctx, '+');
    }

    public static Vb6Value Sub(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        bool ld = l.Type == VT.Date, rd = r.Type == VT.Date;
        if (ld && rd)
            return new Vb6Value(SerialOf(l) - SerialOf(r));      // Date - Date -> Double (day difference)
        if (ld || rd)
            return FromSerial(SerialOf(l) - SerialOf(r), ctx);   // Date - n (or n - Date) -> Date
        return Arith(l, r, ctx, '-');
    }

    public static Vb6Value Mul(Vb6Value l, Vb6Value r, ParserRuleContext? ctx) => Arith(l, r, ctx, '*');

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

        var rt = CoreType(Math.Max(lr, rr));
        if (rt == VT.Currency || rt == VT.Decimal)
        {
            decimal a = ToDecimal(l), b = ToDecimal(r);
            decimal res;
            try { res = op == '+' ? a + b : op == '-' ? a - b : a * b; }
            catch (OverflowException) { throw new VBRunTimeException(ctx, VBStandardError.Overflow); }
            return rt == VT.Currency ? MakeCurrency(res, ctx) : Vb6Value.NewDecimal(res);
        }
        if (rt == VT.Single || rt == VT.Double)
        {
            double a = ToDouble(l), b = ToDouble(r);
            return NarrowDouble(op == '+' ? a + b : op == '-' ? a - b : a * b, rt, ctx);
        }

        long la = ToLong(l), lb = ToLong(r);
        return NarrowLong(op == '+' ? la + lb : op == '-' ? la - lb : la * lb, rt, ctx, l.HasFixedType && r.HasFixedType);
    }

    // ===== / (real division) =====

    public static Vb6Value RealDivide(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        if (!TryToDouble(l, out var a) || !TryToDouble(r, out var b))
            throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
        if (b == 0.0)
            throw new VBRunTimeException(ctx, VBStandardError.DivisionByZero);
        // Result is Single iff the widest core operand is Single (no Long/Double). int/int -> Double.
        int lr = CoreRank(l.Type), rr = CoreRank(r.Type);
        bool single = lr >= 0 && rr >= 0 && Math.Max(lr, rr) == 3;
        // Through NarrowDouble on BOTH paths. The Single path always went through it; the Double path did
        // not, so `1E+308 / 0.0001` handed back an infinity instead of the Overflow vb6.exe raises.
        double res = a / b;
        return NarrowDouble(res, single ? VT.Single : VT.Double, ctx);
    }

    // ===== \ (integer division) and Mod =====

    public static Vb6Value IntDivide(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        long a = RoundToLong(l, ctx), b = RoundToLong(r, ctx);
        if (b == 0)
            throw new VBRunTimeException(ctx, VBStandardError.DivisionByZero);
        return NarrowLong(a / b, IntResultType(l.Type, r.Type), ctx);   // C# / truncates toward zero, as VB6 \
    }

    public static Vb6Value Modulo(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
        long a = RoundToLong(l, ctx), b = RoundToLong(r, ctx);
        if (b == 0)
            throw new VBRunTimeException(ctx, VBStandardError.DivisionByZero);
        return NarrowLong(a % b, IntResultType(l.Type, r.Type), ctx);   // C# % sign follows dividend, as VB6 Mod
    }

    // ===== ^ and unary minus =====

    public static Vb6Value Power(Vb6Value l, Vb6Value r, ParserRuleContext? ctx)
    {
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
        int rank = CoreRank(v.Type);
        if (rank < 0)
        {
            if (!TryToDouble(v, out var d))
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
            return new Vb6Value(-d);
        }
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
            return MakeCurrency(ToDecimal(v), ctx);
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
    /// Every rule here is vb6.exe output (see <i>Coercion-on-store</i> in docs/vb6-fidelity-oracle.md):
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
        if (v.Type == target)
            return v;
        // Before every branch, including String: `Dim s As String : s = Null` is Err 94, not "".
        if (v.IsNull)
            throw new VBRunTimeException(ctx, VBStandardError.InvalidUseOfNull);
        if (target == VT.String)
            return new Vb6Value(v.Value?.ToString() ?? "");
        if (target == VT.Boolean)
            return VB6BuiltIns.CBool(v);
        // Narrow's ToDouble answers 0.0 for a String rather than parsing it, so `Dim i As Integer : i = "12"`
        // would silently store 0 and `i = "abc"` would too. ToNum is the interpreter's own already-pinned
        // string→number rule, Err 13 and all. Routed only for Strings, so the Currency/Decimal paths below
        // keep their decimal precision instead of going through a double.
        if (v.Type == VT.String)
            return Narrow(new Vb6Value(VB6BuiltIns.ToNum(v)), target, ctx);
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

    private static double SerialOf(Vb6Value v) => v.Value is DateTime dt ? dt.ToOADate() : ToDouble(v);

    private static Vb6Value FromSerial(double serial, ParserRuleContext? ctx)
    {
        try { return new Vb6Value(DateTime.FromOADate(serial)); }
        catch (ArgumentException) { throw new VBRunTimeException(ctx, VBStandardError.Overflow); }
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

    private static VT IntResultType(VT l, VT r) =>
        IsByteIntBool(l) && IsByteIntBool(r) ? VT.Integer : VT.Long;

    private static bool IsByteIntBool(VT t) =>
        t == VT.Byte || t == VT.Integer || t == VT.Boolean || t == VT.EmptyVariant;

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
    private static Vb6Value NarrowDouble(double res, VT target, ParserRuleContext? ctx)
    {
        if (double.IsNaN(res))
            throw new VBRunTimeException(ctx, VBStandardError.InvalidProcedureCall);
        if (double.IsInfinity(res))
            throw new VBRunTimeException(ctx, VBStandardError.Overflow);
        if (target == VT.Single)
        {
            var f = (float)res;
            if (float.IsInfinity(f))
                throw new VBRunTimeException(ctx, VBStandardError.Overflow);
            return new Vb6Value(f);
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
