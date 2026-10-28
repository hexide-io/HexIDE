using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

public partial class VB6BuiltIns
{
    private static void RegisterFinancial(Dictionary<string, BuiltinFn> d)
    {
        d["Pmt"] = (_, a, _) => Financial(a, "Pmt");
        d["FV"] = (_, a, _) => Financial(a, "FV");
        d["PV"] = (_, a, _) => Financial(a, "PV");
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

    private static Vb6Value FinancialResult(FinancialNumber number)
    {
        double value = number.ToDouble();
        if (!double.IsFinite(value))
            throw new NotImplementedException("Financial overflow errors are unmeasured (#2523)");
        return new Vb6Value(value);
    }
}
