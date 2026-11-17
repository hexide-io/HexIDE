using System;
using System.Collections.Generic;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Interpreter;

// Phase 3 — Math intrinsics. Abs/Int/Fix preserve the operand type; Sgn -> Integer; Sqr/trig/Exp/Log -> Double
// (all verified against vb6.exe). Rnd reproduces VB6's exact 24-bit LCG so the sequence is bit-identical.
public partial class VB6BuiltIns
{
    // Per-run RNG state. Seed 0x50000 and LCG verified against vb6.exe: LCG(0x50000)=11837123 -> 0.7055475.
    private uint rndSeed = 0x50000;

    private static void RegisterMath(Dictionary<string, BuiltinFn> d)
    {
        d["Abs"]   = (_, a, _) => Abs(a[0]);
        d["Int"]   = (_, a, _) => Whole(a[0], truncate: false);   // floor toward -inf
        d["Fix"]   = (_, a, _) => Whole(a[0], truncate: true);    // truncate toward zero
        d["Sgn"]   = (_, a, _) => new Vb6Value(Math.Sign(AsDouble(a[0])));
        d["Sqr"]   = (_, a, _) => { var x = AsDouble(a[0]); if (x < 0) throw InvalidCall(); return new Vb6Value(Math.Sqrt(x)); };
        d["Sin"]   = (_, a, _) => new Vb6Value(Math.Sin(AsDouble(a[0])));
        d["Cos"]   = (_, a, _) => new Vb6Value(Math.Cos(AsDouble(a[0])));
        d["Tan"]   = (_, a, _) => new Vb6Value(Math.Tan(AsDouble(a[0])));
        d["Atn"]   = (_, a, _) => new Vb6Value(Math.Atan(AsDouble(a[0])));
        // Exp is the one intrinsic here that can overflow a Double from an ordinary-looking argument —
        // Exp(1000) is Err 6, measured. Sqr and Log already guard their domains; Sin/Cos/Atn cannot leave
        // their range, and Tan's asymptote is unreachable in binary floating point.
        d["Exp"]   = (_, a, _) => Finite(Math.Exp(AsDouble(a[0])));
        d["Log"]   = (_, a, _) => { var x = AsDouble(a[0]); if (x <= 0) throw InvalidCall(); return new Vb6Value(Math.Log(x)); };
        d["Round"] = (_, a, _) => new Vb6Value(RoundHalfEven(AsDouble(a[0]), Supplied(a, 1) ? AsInt(a[1]) : 0));
        d["Rnd"]   = (self, a, _) => new Vb6Value(self.Rnd(Supplied(a, 0) ? (float)AsDouble(a[0]) : 1f));
        var roundDouble = d["Round"];
        d["Round"] = (self, a, text) => RoundKeepingType(a, () => roundDouble(self, a, text));
    }

    // Round keeps a Decimal's, a Currency's and a Single's type (#4085). A Decimal or a Currency rounds its exact value
    // half to even: Round(CDec(2.345), 2) is 2.34 and Round(CCur(2.345), 2) is 2.34, where the Double 2.345 gives 2.35.
    // A Single goes through the Double path, as a Double does, and comes back a Single; the measured rows don't separate
    // that from scaling in Single. Every other argument takes the Double path unchanged, since vb6.exe's result type for
    // them isn't measured. vb6.exe SP6, native and p-code, on a-double-or-a-single-converted-to-decimal.md (oracle PR
    // #4017). The Double path is the registration above, reached through the delegate so that it stays the one place
    // that decides how a Double rounds.
    private static Vb6Value RoundKeepingType(IReadOnlyList<Vb6Value> a, Func<Vb6Value> roundDouble)
    {
        var digits = Supplied(a, 1) ? AsInt(a[1]) : 0;
        return a[0].Value switch
        {
            decimal m when a[0].Type == VT.Currency => Vb6Value.NewCurrency(Math.Round(m, digits, MidpointRounding.ToEven)),
            decimal m => Vb6Value.NewDecimal(Math.Round(m, digits, MidpointRounding.ToEven)),
            float => new Vb6Value((float)AsDouble(roundDouble())),
            _ => roundDouble(),
        };
    }

    private static Vb6Value Abs(Vb6Value v) => v.Value switch
    {
        byte b => new Vb6Value(b),
        int i => new Vb6Value(Math.Abs(i)),
        long l when v.Type == VT.LongLong => l == long.MinValue
            ? throw new VBRunTimeException(VBStandardError.Overflow)
            : Vb6Value.NewLongLong(Math.Abs(l)),
        long l => new Vb6Value(Math.Abs(l)),
        float f => new Vb6Value(Math.Abs(f)),
        double d => new Vb6Value(Math.Abs(d)),
        decimal m => v.Type == VT.Currency ? Vb6Value.NewCurrency(Math.Abs(m)) : Vb6Value.NewDecimal(Math.Abs(m)),
        _ => new Vb6Value(Math.Abs(AsDouble(v)))
    };

    /// <summary>A Double result, or the error VB6 raises rather than representing it. VB6 has no Infinity
    /// and no NaN: an infinite result is Err 6 and a NaN is Err 5 (measured — see VbNumeric.NarrowDouble,
    /// which applies the same rule to the operators).</summary>
    private static Vb6Value Finite(double d)
    {
        if (double.IsNaN(d)) throw InvalidCall();
        if (double.IsInfinity(d)) throw new VBRunTimeException(VBStandardError.Overflow);
        return new Vb6Value(d);
    }

    // VB6's Round scales by 10^digits in Double arithmetic, rounds that half to even, and scales back. Measured on vb6.exe
    // SP6: Round(2.675, 2) is 2.68 (267.5), Round(0.015, 2) is 0.02 (1.5) (PR #4017), and Round(2.345, 2) is 2.35
    // (234.50000000000003), where half to even on the 15-digit Decimal value would give 2.34. .NET 10's
    // Math.Round(double, digits) worked the same way; .NET 11's rounds the exact binary value, giving 2.67 and 0.01.
    // Digits outside 0-15, and values too big to have a fraction, keep the host's handling, errors included.
    private static readonly double[] PowersOfTen =
        [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];

    private static double RoundHalfEven(double x, int digits)
    {
        if (digits is < 0 or > 15 || !double.IsFinite(x) || Math.Abs(x) >= 1e16)
            return Math.Round(x, digits, MidpointRounding.ToEven);
        var power = PowersOfTen[digits];
        return Math.Round(x * power, MidpointRounding.ToEven) / power;
    }

    private static Vb6Value Whole(Vb6Value v, bool truncate)
    {
        if (v.Value is int or long or byte) return v;   // already integral
        double d = AsDouble(v);
        double r = truncate ? Math.Truncate(d) : Math.Floor(d);
        return v.Value switch
        {
            float => new Vb6Value((float)r),
            decimal => v.Type == VT.Currency ? Vb6Value.NewCurrency((decimal)r) : Vb6Value.NewDecimal((decimal)r),
            // A Date stays a Date, cut on its serial: Int and Fix of 1999-12-31 23:59:59.6 are both 31/12/1999, where
            // CStr of the date itself is 01/01/2000 (measured, #783). Only Int can leave the range, from a time on 1 January 100.
            VbDate when VbDate.InRange(r) => new Vb6Value(new VbDate(r)),
            VbDate => throw new NotImplementedException(
                "Int of a time on 1 January 100 is not implemented: the day before is outside the Date range, and what vb6.exe does was not measured (#783)"),
            _ => new Vb6Value(r)
        };
    }

    // VB6's Rnd: x>0 (or omitted) advances the LCG; x=0 returns the last value without advancing; x<0 reseeds
    // by folding the Single bits and high byte (measured, #2117). Returns Single.
    private float Rnd(float x)
    {
        if (x == 0f)
            return rndSeed / 16777216f;                 // last, no advance
        if (x < 0f)
        {
            uint bits = BitConverter.SingleToUInt32Bits(x);
            rndSeed = (bits + (bits >> 24)) & 0xFFFFFF;
        }
        rndSeed = (rndSeed * 0x43FD43FDu + 0xC39EC3u) & 0xFFFFFF;
        return rndSeed / 16777216f;
    }

    // An explicit Randomize seed folds the high Double bits, keeping the state's low byte (#2117).
    // The unmeasured argumentless clock seeding retains its existing behaviour.
    internal void Reseed(double? number)
    {
        if (number is { } n)
        {
            uint high = (uint)(BitConverter.DoubleToUInt64Bits(n) >> 32);
            rndSeed = (((high >> 16) ^ (high & 0xFFFF)) << 8) | (rndSeed & 0xFF);
        }
        else
        {
            long bits = DateTime.Now.Ticks;
            rndSeed = (uint)((bits ^ (bits >> 24)) & 0xFFFFFF);
        }
    }
}
