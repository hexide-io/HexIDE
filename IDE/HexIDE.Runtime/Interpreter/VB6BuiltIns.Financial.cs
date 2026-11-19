using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public partial class VB6BuiltIns
{
    private static void RegisterFinancial(Dictionary<string, BuiltinFn> d)
    {
        d["Pmt"] = (_, a, _) => Financial(a, "Pmt");
        d["FV"] = (_, a, _) => Financial(a, "FV");
        foreach (var name in new[] { "NPer", "IPmt", "PPmt", "Rate" })
            d[name] = (_, a, _) => FinancialAdditional(a, name);
        d["PV"] = (_, a, _) => Financial(a, "PV");
        d["NPV"] = (_, a, _) => FinancialNpv(a);
        d["IRR"] = (_, a, _) => FinancialCashFlow(a, false);
        d["MIRR"] = (_, a, _) => FinancialCashFlow(a, true);
    }

    // Argument diagnostics and Variant conversions measured on SP6, native and p-code (#4883).
    private static void FinancialArguments(IReadOnlyList<Vb6Value> args, int required, int maximum)
    {
        if (args.Count > maximum)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        if (args.Count < required)
            throw new VBRunTimeException(VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
        for (int i = 0; i < required; i++)
            if (args[i].IsMissing && !args[i].HeldAsVariant)
                throw new VBRunTimeException(VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
    }

    private static double FinancialArgument(Vb6Value value)
    {
        if (value.IsMissing || value.Type == Vb6Value.ValueType.Error)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        value = RefuseNonScalar(value);
        double result = (double)NarrowNum(value, Vb6Value.ValueType.Double).Value!;
        // Even timing flags must refuse non-finite arguments; they are not simply nonzero (#4883).
        _ = FinancialNumber.FromDouble(result);
        return result;
    }

    private static VBArray FinancialArray(Vb6Value value)
    {
        if (value.HeldAsVariant || value.Value is not VBArray array || array.ElementType != Vb6Value.ValueType.Double)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (!array.IsDefined || array.Rank != 1 || array.Length() == 0) throw InvalidCall();
        return array;
    }

    private static Vb6Value FinancialCashFlow(IReadOnlyList<Vb6Value> args, bool modified)
    {
        string name = modified ? "MIRR" : "IRR";
        NotImplementedException Unmeasured() => new($"{name}: this argument or arithmetic domain is unmeasured (#4883)");
        int required = modified ? 3 : 1;
        FinancialArguments(args, required, modified ? 3 : 2);
        var array = FinancialArray(args[0]);
        var rates = new double[modified ? 2 : 1];
        if (!modified) rates[0] = 0.1;
        for (int i = 1; i < args.Count; i++)
        {
            if (!Supplied(args, i))
            {
                if (modified) rates[i - 1] = FinancialArgument(args[i]);
                continue;
            }
            rates[i - 1] = FinancialArgument(args[i]);
            // Non-finite arguments remain deliberately refused (#4883).
            _ = FinancialNumber.FromDouble(rates[i - 1]);
        }
        var flows = new List<FinancialNumber>();
        bool positive = false, negative = false, zero = false;
        foreach (var value in array.EnumerateElements())
        {
            double flow = (double)value.Value!;
            positive |= flow > 0;
            negative |= flow < 0;
            zero |= flow == 0;
            flows.Add(FinancialNumber.FromDouble(flow));
        }
        FinancialNumber Extended(FinancialNumber value) => value.RoundSignificand(64);
        FinancialNumber DoubleSpill(FinancialNumber value)
        {
            double result = value.ToDouble();
            if (!double.IsFinite(result)) throw new VBRunTimeException(VBStandardError.Overflow);
            return FinancialNumber.FromDouble(result);
        }
        // #2525's authorized closest models: Horner mask 9 for IRR, mask 84 for MIRR.
        FinancialNumber Residual(FinancialNumber rate, int sign = 0)
        {
            var basis = modified ? Extended(1 + rate) : DoubleSpill(1 + rate);
            if (basis.ToDouble() == 0) throw new VBRunTimeException(VBStandardError.DivisionByZero);
            FinancialNumber total = 0;
            for (int i = flows.Count - 1; i >= 0; i--)
            {
                var value = flows[i];
                if (sign != 0 && Math.Sign(value.ToDouble()) != sign) value = 0;
                var sum = modified ? Extended(total + value) : DoubleSpill(total + value);
                total = modified ? DoubleSpill(sum / basis) : Extended(sum / basis);
            }
            return total;
        }
        if (!modified)
        {
            if (positive && !negative && !zero) throw InvalidCall();
            if (!positive || !negative || rates[0] <= -1) throw InvalidCall();
            // A zero guess still has no measured nonconvergence answer (#4883).
            if (rates[0] == 0) throw Unmeasured();
            var x = FinancialNumber.FromDouble(rates[0]);
            var y = x / 2;
            try
            {
                for (int i = 0; i < 40; i++)
                {
                    var a = Extended(Residual(x));
                    var b = Extended(Residual(y));
                    if (Math.Abs(b.ToDouble()) < 1e-7)
                    {
                        // Discounting alone tends to zero at an infinite rate, without a root.
                        // Validate the residual before the first discount too (#4883).
                        if (Math.Abs((b * (1 + y)).ToDouble()) >= 1e-7) throw InvalidCall();
                        return new Vb6Value(y.ToDouble());
                    }
                    if ((a - b).ToDouble() == 0) throw InvalidCall();
                    var next = Extended(y - b * (y - x) / (b - a));
                    x = y;
                    y = next;
                }
            }
            catch (VBRunTimeException) { throw InvalidCall(); }
            throw InvalidCall();
        }
        if (rates[1] == -1) throw InvalidCall();
        if (rates[0] == -1) throw InvalidCall();
        if (positive && !negative && !zero) throw new VBRunTimeException(VBStandardError.DivisionByZero);
        if (flows.Count < 2) throw InvalidCall();
        if (!negative) throw new VBRunTimeException(VBStandardError.DivisionByZero);
        if (!positive) return new Vb6Value(-1.0);
        var finance = FinancialNumber.FromDouble(rates[0]);
        var reinvest = FinancialNumber.FromDouble(rates[1]);
        // #4883's finance-rate overflow occurs in the cumulative discount power.
        _ = DoubleSpill((1 + finance).Pow(flows.Count));
        var neg = Residual(finance, -1);
        var pos = Residual(reinvest, 1);
        var terminal = DoubleSpill((1 + reinvest).Pow(flows.Count));
        var denominator = Extended(-neg * (1 + finance));
        if (denominator.ToDouble() == 0) throw new VBRunTimeException(VBStandardError.DivisionByZero);
        var ratio = DoubleSpill(pos * terminal / denominator);
        var root = Extended(ratio.PowReciprocal(flows.Count - 1));
        return new Vb6Value(DoubleSpill(root - 1).ToDouble());
    }

    private static Vb6Value FinancialNpv(IReadOnlyList<Vb6Value> args)
    {
        FinancialArguments(args, 2, 2);
        double rate = FinancialArgument(args[0]);
        var array = FinancialArray(args[1]);
        if (rate == -1) throw InvalidCall();
        var basis = 1 + FinancialNumber.FromDouble(rate);
        FinancialNumber power = 1, total = 0;
        // #2525 matches only when each cumulative power spills, with the base and sum retained.
        foreach (var value in array.EnumerateElements())
        {
            double discount = (power * basis).ToDouble();
            if (discount == 0) throw new VBRunTimeException(VBStandardError.DivisionByZero);
            if (!double.IsFinite(discount)) throw new VBRunTimeException(VBStandardError.Overflow);
            power = FinancialNumber.FromDouble(discount);
            total += FinancialNumber.FromDouble((double)value.Value!) / power;
        }
        double result = total.ToDouble();
        if (!double.IsFinite(result))
            throw new VBRunTimeException(VBStandardError.Overflow);
        return new Vb6Value(result);
    }

    // Positional calls measured in vb6.exe SP6, native and p-code (#2523). The base is a Double
    // spill; the remaining arithmetic is retained until the result is rounded once to Double.
    private static Vb6Value Financial(IReadOnlyList<Vb6Value> args, string name)
    {
        FinancialArguments(args, 3, 5);
        var values = new double[5];
        for (int i = 0; i < args.Count; i++)
            if (i < 3 || Supplied(args, i)) values[i] = FinancialArgument(args[i]);
        double rate = values[0], periods = values[1];
        var r = FinancialNumber.FromDouble(rate);
        var n = FinancialNumber.FromDouble(periods);
        var a = FinancialNumber.FromDouble(values[2]);
        var b = FinancialNumber.FromDouble(values[3]);
        if (name == "Pmt" && periods == 0) throw InvalidCall();
        if (rate == 0)
            return FinancialResult(name == "Pmt" ? -(a + b) / n : name == "FV" ? -(b + a * n) : -(a * n + b));
        var basis = FinancialNumber.FromDouble(1 + rate);
        var power = basis.Pow(periods);
        var timing = values[4] == 0 ? (FinancialNumber)1 : basis;
        var result = name switch
        {
            "Pmt" => -(r * (a * power + b)) / ((power - 1) * timing),
            "FV" => -(b * power + a * timing * (power - 1) / r),
            _ => -(b + a * timing * (power - 1) / r) / power
        };
        return FinancialResult(result);
    }

    private static FinancialNumber Spill(FinancialNumber value)
    {
        double result = value.ToDouble();
        if (!double.IsFinite(result)) throw new VBRunTimeException(VBStandardError.Overflow);
        return FinancialNumber.FromDouble(result);
    }

    private static Vb6Value FinancialAdditional(IReadOnlyList<Vb6Value> args, string name)
    {
        bool interest = name is "IPmt" or "PPmt";
        int required = interest ? 4 : 3;
        int maximum = name == "NPer" ? 5 : 6;
        FinancialArguments(args, required, maximum);
        var v = new double[maximum];
        if (name == "Rate") v[5] = 0.1;
        for (int i = 0; i < args.Count; i++)
            if (i < required || Supplied(args, i)) v[i] = FinancialArgument(args[i]);
        var r = FinancialNumber.FromDouble(v[0]);
        var a = FinancialNumber.FromDouble(v[1]);
        var b = FinancialNumber.FromDouble(v[2]);
        var c = FinancialNumber.FromDouble(v[3]);
        if (name == "NPer")
        {
            if (v[0] == 0)
            {
                if (v[1] == 0) throw InvalidCall();
                return FinancialResult(-(b + c) / a);
            }
            if (v[0] <= -1) throw InvalidCall();
            var basis = 1 + r;
            var timedPayment = a * (v[4] == 0 ? (FinancialNumber)1 : basis);
            var denominator = Spill(timedPayment + b * r);
            if (denominator.ToDouble() == 0) throw InvalidCall();
            var ratio = (timedPayment - c * r) / denominator;
            // #4883's tiny rate loses the increment in a 64-bit sum before Log: 0/0 raises 6.
            // Ordinary measured bases are exact at this precision, preserving their fitted results.
            return FinancialResult(ratio.Log() / basis.RoundSignificand(64).Log());
        }
        if (name == "Rate") return FinancialRate(v);
        double period = v[1], periods = v[2];
        if (period < 1 || period > periods) throw InvalidCall();
        // #4883 measures period 2 at rate -1; leave the other previously refused periods alone.
        if (v[0] == -1 && period != 2)
            throw new NotImplementedException("Other interest/principal periods at rate -1 are unmeasured (#4883)");
        var pv = c;
        var fv = FinancialNumber.FromDouble(v[4]);
        bool beginning = v[5] != 0;
        bool divisions = name == "PPmt";
        var basisRounded = FinancialNumber.FromDouble(1 + v[0]);
        FinancialNumber Divide(FinancialNumber x, FinancialNumber y) => divisions ? Spill(x / y) : x / y;
        FinancialNumber Payment()
        {
            if (v[0] == 0) return -Divide(pv + fv, b);
            var power = basisRounded.Pow(periods);
            // #4883's 1E+308 IPmt/PPmt rows differ by 1 ULP in this exact-intermediate
            // model. Refuse principals whose powered value exceeds Double, pending a matching model.
            if (!double.IsFinite((pv * power).ToDouble()))
                throw new NotImplementedException("This interest/principal precision domain is not implemented (#4883)");
            return -Divide(r * (pv * power + fv), (power - 1) * (beginning ? basisRounded : 1));
        }
        FinancialNumber Balance(double n, FinancialNumber payment)
        {
            if (v[0] == 0) return -(pv + payment * FinancialNumber.FromDouble(n));
            var power = basisRounded.Pow(n);
            return -(pv * power + Divide(payment * (beginning ? basisRounded : 1) * (power - 1), r));
        }
        var payment = Payment();
        FinancialNumber interestValue;
        if (name == "IPmt")
        {
            if (beginning && period == 1) return new Vb6Value(0.0);
            if (v[0] is 0 or -1) return new Vb6Value(-0.0);
            var roundedPayment = Spill(payment);
            interestValue = (beginning ? Balance(period - 2, roundedPayment) - roundedPayment
                : Balance(period - 1, roundedPayment)) * r;
            return FinancialResult(interestValue);
        }
        interestValue = beginning && period == 1 ? 0 : Balance(period - 1, payment) * r;
        if (beginning) interestValue = Divide(interestValue, basisRounded);
        var principal = payment - interestValue;
        return principal.ToDouble() == 0 ? new Vb6Value(0.0) : FinancialResult(principal);
    }

    private static Vb6Value FinancialRate(double[] v)
    {
        double periods = v[0];
        if (periods == 0 || v[5] is -1 or -2) throw InvalidCall();
        var payment = FinancialNumber.FromDouble(v[1]);
        var pv = FinancialNumber.FromDouble(v[2]);
        var fv = FinancialNumber.FromDouble(v[3]);
        FinancialNumber Residual(FinancialNumber rate)
        {
            if (rate.ToDouble() == 0) return Spill(pv + payment * FinancialNumber.FromDouble(periods) + fv);
            var basis = Spill(1 + rate);
            var power = basis.Pow(periods, signedIntegerBase: true);
            return Spill(pv * power + payment * (v[4] == 0 ? (FinancialNumber)1 : basis) * (power - 1) / rate + fv);
        }
        var x = FinancialNumber.FromDouble(v[5]);
        var y = x / 2;
        // The measured rows require at least 23 iterations; they do not pin this cap.
        for (int i = 0; i < 40; i++)
        {
            var a = Residual(x);
            var b = Residual(y);
            if (Math.Abs(b.ToDouble()) < 1e-7) return FinancialResult(y);
            if (a.ToDouble() == b.ToDouble()) break;
            var next = Spill(y - b * (y - x) / (b - a));
            x = y;
            y = next;
        }
        throw InvalidCall();
    }

    private static Vb6Value FinancialResult(FinancialNumber number)
    {
        double value = number.ToDouble();
        if (!double.IsFinite(value))
            throw new VBRunTimeException(VBStandardError.Overflow);
        return new Vb6Value(value);
    }
}
