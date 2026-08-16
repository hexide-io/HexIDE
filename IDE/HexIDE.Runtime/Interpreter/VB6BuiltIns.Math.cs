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
        d["Exp"]   = (_, a, _) => new Vb6Value(Math.Exp(AsDouble(a[0])));
        d["Log"]   = (_, a, _) => { var x = AsDouble(a[0]); if (x <= 0) throw InvalidCall(); return new Vb6Value(Math.Log(x)); };
        d["Round"] = (_, a, _) => new Vb6Value(Math.Round(AsDouble(a[0]), a.Count >= 2 ? AsInt(a[1]) : 0, MidpointRounding.ToEven));
        d["Rnd"]   = (self, a, _) => new Vb6Value(self.Rnd(a.Count >= 1 ? (float)AsDouble(a[0]) : 1f));
    }

    private static Vb6Value Abs(Vb6Value v) => v.Value switch
    {
        byte b => new Vb6Value(b),
        int i => new Vb6Value(Math.Abs(i)),
        long l => new Vb6Value(Math.Abs(l)),
        float f => new Vb6Value(Math.Abs(f)),
        double d => new Vb6Value(Math.Abs(d)),
        decimal m => v.Type == VT.Currency ? Vb6Value.NewCurrency(Math.Abs(m)) : Vb6Value.NewDecimal(Math.Abs(m)),
        _ => new Vb6Value(Math.Abs(AsDouble(v)))
    };

    private static Vb6Value Whole(Vb6Value v, bool truncate)
    {
        if (v.Value is int or long or byte) return v;   // already integral
        double d = AsDouble(v);
        double r = truncate ? Math.Truncate(d) : Math.Floor(d);
        return v.Value switch
        {
            float => new Vb6Value((float)r),
            decimal => v.Type == VT.Currency ? Vb6Value.NewCurrency((decimal)r) : Vb6Value.NewDecimal((decimal)r),
            _ => new Vb6Value(r)
        };
    }

    // VB6's Rnd: x>0 (or omitted) advances the LCG; x=0 returns the last value without advancing; x<0 reseeds
    // deterministically (the reseed value is not bit-identical to VB6, but is repeatable). Returns Single.
    private float Rnd(float x)
    {
        if (x == 0f)
            return rndSeed / 16777216f;                 // last, no advance
        if (x < 0f)
            rndSeed = BitConverter.SingleToUInt32Bits(x) & 0xFFFFFF;
        rndSeed = (rndSeed * 0x43FD43FDu + 0xC39EC3u) & 0xFFFFFF;
        return rndSeed / 16777216f;
    }

    // Randomize [number]: reseed. Deterministic from `number` (not bit-identical to VB6's fold), or from the
    // wall clock when omitted. Rnd's own fixed-seed sequence is vb6-exact; Randomize only varies the start.
    internal void Reseed(double? number)
    {
        long bits = number is { } n ? BitConverter.DoubleToInt64Bits(n) : DateTime.Now.Ticks;
        rndSeed = (uint)((bits ^ (bits >> 24)) & 0xFFFFFF);
    }
}
