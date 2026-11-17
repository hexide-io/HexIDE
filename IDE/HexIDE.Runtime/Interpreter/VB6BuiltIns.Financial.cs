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

    private static Vb6Value FinancialCashFlow(IReadOnlyList<Vb6Value> args, bool modified)
    {
        string name = modified ? "MIRR" : "IRR";
        NotImplementedException Unmeasured() => new($"{name}: this argument or arithmetic domain is unmeasured (#2525)");
        int required = modified ? 3 : 1;
        if (args.Count < required || args.Count > (modified ? 3 : 2) || !Supplied(args, 0))
            throw Unmeasured();
        if (args[0].Value is not VBArray array || array.ElementType != Vb6Value.ValueType.Double
            || !array.IsDefined || array.Rank != 1 || array.Length() == 0)
            throw Unmeasured();
        var rates = new double[modified ? 2 : 1];
        if (!modified) rates[0] = 0.1;
        for (int i = 1; i < args.Count; i++)
        {
            if (!Supplied(args, i))
            {
                if (modified) throw Unmeasured();
                continue;
            }
            if (args[i].IsNull || args[i].Value is VbDate
                || !Vb6Value.TryNumericToDouble(args[i], out rates[i - 1]) || !double.IsFinite(rates[i - 1]))
                throw Unmeasured();
        }
        var flows = new List<FinancialNumber>();
        bool positive = false, negative = false, zero = false;
        foreach (var value in array.EnumerateElements())
        {
            double flow = (double)value.Value!;
            if (!double.IsFinite(flow)) throw Unmeasured();
            positive |= flow > 0;
            negative |= flow < 0;
            zero |= flow == 0;
            flows.Add(FinancialNumber.FromDouble(flow));
        }
        FinancialNumber Extended(FinancialNumber value) => value.RoundSignificand(64);
        FinancialNumber DoubleSpill(FinancialNumber value)
        {
            double result = value.ToDouble();
            if (!double.IsFinite(result)) throw Unmeasured();
            return FinancialNumber.FromDouble(result);
        }
        // #2525's authorized closest models: Horner mask 9 for IRR, mask 84 for MIRR.
        FinancialNumber Residual(FinancialNumber rate, int sign = 0)
        {
            var basis = modified ? Extended(1 + rate) : DoubleSpill(1 + rate);
            if (basis.ToDouble() == 0) throw Unmeasured();
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
            if (!positive || !negative) throw Unmeasured();
            var x = FinancialNumber.FromDouble(rates[0]);
            var y = x / 2;
            for (int i = 0; i < 40; i++)
            {
                var a = Extended(Residual(x));
                var b = Extended(Residual(y));
                if (Math.Abs(b.ToDouble()) < 1e-7) return new Vb6Value(y.ToDouble());
                if ((a - b).ToDouble() == 0) throw Unmeasured();
                var next = Extended(y - b * (y - x) / (b - a));
                x = y;
                y = next;
            }
            throw Unmeasured();
        }
        if (rates[1] == -1) throw Unmeasured();
        if (rates[0] == -1) throw InvalidCall();
        if (positive && !negative && !zero) throw new VBRunTimeException(VBStandardError.DivisionByZero);
        if (!positive || !negative || flows.Count < 2 || flows.Count > 10000) throw Unmeasured();
        var finance = FinancialNumber.FromDouble(rates[0]);
        var reinvest = FinancialNumber.FromDouble(rates[1]);
        var neg = Residual(finance, -1);
        var pos = Residual(reinvest, 1);
        if ((1 + reinvest).ToDouble() <= 0) throw Unmeasured();
        var terminal = DoubleSpill((1 + reinvest).Pow(flows.Count));
        var denominator = Extended(-neg * (1 + finance));
        if (denominator.ToDouble() == 0) throw Unmeasured();
        var ratio = DoubleSpill(pos * terminal / denominator);
        var root = Extended(ratio.PowReciprocal(flows.Count - 1));
        return new Vb6Value(DoubleSpill(root - 1).ToDouble());
    }

    private static Vb6Value FinancialNpv(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 2 || !Supplied(args, 0) || !Supplied(args, 1))
            throw new NotImplementedException("NPV argument-count and missing-argument errors are unmeasured (#2525)");
        if (args[0].IsNull || args[0].Value is VbDate || !Vb6Value.TryNumericToDouble(args[0], out double rate)
            || args[1].Value is not VBArray array
            || array.ElementType != Vb6Value.ValueType.Double)
            throw new NotImplementedException("This NPV argument conversion is unmeasured (#2525)");
        if (!array.IsDefined) throw InvalidCall();
        if (array.Rank != 1 || array.Length() == 0)
            throw new NotImplementedException("Empty and multidimensional NPV arrays are unmeasured (#2525)");
        if (rate == -1) throw InvalidCall();
        var basis = 1 + FinancialNumber.FromDouble(rate);
        FinancialNumber power = 1, total = 0;
        // #2525 matches only when each cumulative power spills, with the base and sum retained.
        foreach (var value in array.EnumerateElements())
        {
            double discount = (power * basis).ToDouble();
            if (discount == 0 || !double.IsFinite(discount))
                throw new NotImplementedException("This NPV arithmetic domain is unmeasured (#2525)");
            power = FinancialNumber.FromDouble(discount);
            total += FinancialNumber.FromDouble((double)value.Value!) / power;
        }
        double result = total.ToDouble();
        if (!double.IsFinite(result))
            throw new NotImplementedException("NPV overflow errors are unmeasured (#2525)");
        return new Vb6Value(result);
    }

    // Positional calls measured in vb6.exe SP6, native and p-code (#2523). The base is a Double
    // spill; the remaining arithmetic is retained until the result is rounded once to Double.
    private static Vb6Value Financial(IReadOnlyList<Vb6Value> args, string name)
    {
        if (args.Count < 3 || args.Count > 5)
            throw new NotImplementedException("Financial argument-count errors are unmeasured (#2523)");
        var values = new double[5];
        for (int i = 0; i < args.Count; i++)
        {
            if (!Supplied(args, i))
            {
                if (i < 3) throw new NotImplementedException("Missing required financial arguments are unmeasured (#2523)");
                continue;
            }
            if (args[i].IsNull)
            {
                if (name == "Pmt" && i == 0) throw InvalidUseOfNull();
                throw new NotImplementedException("This Null financial argument is unmeasured (#2523)");
            }
            if (args[i].Value is VbDate)
                throw new NotImplementedException("Date financial arguments are unmeasured (#2523)");
            if (!Vb6Value.TryNumericToDouble(args[i], out values[i]))
            {
                if (name == "Pmt" && i == 0 && args[i].Type == Vb6Value.ValueType.String)
                {
                    try { values[i] = AsDouble(args[i]); }
                    catch (VBRunTimeException)
                    {
                        throw new NotImplementedException("This financial String conversion is unmeasured (#2523)");
                    }
                }
                else throw new NotImplementedException("This financial argument conversion is unmeasured (#2523)");
            }
        }
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

    private static FinancialNumber Spill(FinancialNumber value) => FinancialNumber.FromDouble(value.ToDouble());

    private static Vb6Value FinancialAdditional(IReadOnlyList<Vb6Value> args, string name)
    {
        bool interest = name is "IPmt" or "PPmt";
        int required = interest ? 4 : 3;
        int maximum = name == "NPer" ? 5 : 6;
        if (args.Count < required || args.Count > maximum)
            throw new NotImplementedException("Financial argument-count errors are unmeasured (#2523)");
        var v = new double[maximum];
        if (name == "Rate") v[5] = 0.1;
        for (int i = 0; i < args.Count; i++)
        {
            if (!Supplied(args, i))
            {
                if (i < required) throw new NotImplementedException("Missing required financial arguments are unmeasured (#2523)");
                continue;
            }
            if (args[i].IsNull || args[i].Value is VbDate || !Vb6Value.TryNumericToDouble(args[i], out v[i]))
                throw new NotImplementedException("This financial argument conversion is unmeasured (#2523)");
        }
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
            var basis = 1 + r;
            var timedPayment = a * (v[4] == 0 ? (FinancialNumber)1 : basis);
            var ratio = (timedPayment - c * r) / Spill(timedPayment + b * r);
            return FinancialResult(ratio.Log() / basis.Log());
        }
        if (name == "Rate") return FinancialRate(v);
        double period = v[1], periods = v[2];
        if (period < 1 || period > periods) throw InvalidCall();
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
            if (v[0] == 0) return new Vb6Value(-0.0);
            var roundedPayment = Spill(payment);
            interestValue = (beginning ? Balance(period - 2, roundedPayment) - roundedPayment
                : Balance(period - 1, roundedPayment)) * r;
            return FinancialResult(interestValue);
        }
        interestValue = beginning && period == 1 ? 0 : Balance(period - 1, payment) * r;
        if (beginning) interestValue = Divide(interestValue, basisRounded);
        return FinancialResult(payment - interestValue);
    }

    private static Vb6Value FinancialRate(double[] v)
    {
        double periods = v[0];
        if (periods == 0) throw InvalidCall();
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
            throw new NotImplementedException("Financial overflow errors are unmeasured (#2523)");
        return new Vb6Value(value);
    }
}
