using System;
using System.Collections.Generic;
using System.Numerics;

namespace HexIDE.Runtime.Interpreter;

public partial class VB6BuiltIns
{
    private static void RegisterDepreciation(Dictionary<string, BuiltinFn> d)
    {
        d["DDB"] = (_, a, _) =>
        {
            FinancialArguments(a, 4, 5);
            var cost = FinancialNumber.FromDouble(FinancialArgument(a[0]));
            var salvage = FinancialNumber.FromDouble(FinancialArgument(a[1]));
            var life = FinancialArgument(a[2]);
            var period = FinancialArgument(a[3]);
            var factor = Supplied(a, 4) ? FinancialArgument(a[4]) : 2;
            if (life == 0 || period < 1 || period > life || factor == 0 || !double.IsFinite((cost - salvage).ToDouble())) throw InvalidCall();
            var rate = FinancialNumber.FromDouble(factor) / FinancialNumber.FromDouble(life);
            // #2524's approved closest model spills only the base to Double.
            var basis = FinancialNumber.FromDouble((1 - rate).ToDouble());
            // These DDB power rows remain unmeasured; #4883 measures payment powers, not DDB's.
            if (basis.ToDouble() <= 0 || Math.Abs(period - 1) > 10000)
                throw new NotImplementedException("This DDB power domain is unmeasured (#4883)");
            var book = cost * basis.Pow(period - 1);
            var amount = book * rate;
            return Finite(Math.Min(amount.ToDouble(), (book - salvage).ToDouble()));
        };
        d["SLN"] = (_, a, _) =>
        {
            FinancialArguments(a, 3, 3);
            var cost = DepreciationNumber.From(FinancialArgument(a[0]));
            var salvage = DepreciationNumber.From(FinancialArgument(a[1]));
            var life = FinancialArgument(a[2]);
            if (life == 0) throw InvalidCall();
            return Finite(((cost - salvage) / DepreciationNumber.From(life)).ToDouble());
        };
        d["SYD"] = (_, a, _) =>
        {
            FinancialArguments(a, 4, 4);
            var cost = DepreciationNumber.From(FinancialArgument(a[0]));
            var salvage = DepreciationNumber.From(FinancialArgument(a[1]));
            var life = FinancialArgument(a[2]);
            var period = FinancialArgument(a[3]);
            if (period < 1 || period > life || !double.IsFinite((cost - salvage).ToDouble())) throw InvalidCall();
            var n = DepreciationNumber.From(life);
            var remaining = n - DepreciationNumber.From(period) + DepreciationNumber.From(1);
            return Finite(((cost - salvage) * remaining * DepreciationNumber.From(2)
                / (n * (n + DepreciationNumber.From(1)))).ToDouble());
        };
    }

    // #2524's measured SLN/SYD rows match exact arithmetic with no Double spills before the result.
    // Preserve the input Double's binary value, rather than converting it to decimal or rounding each operation.
    private readonly record struct DepreciationNumber
    {
        private readonly BigInteger numerator;
        private readonly BigInteger denominator;

        private DepreciationNumber(BigInteger numerator, BigInteger denominator)
        {
            var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator) * denominator.Sign;
            this.numerator = numerator / divisor;
            this.denominator = denominator / divisor;
        }

        public static DepreciationNumber From(double value)
        {
            // Non-finite inputs require API construction in VB6; deliberately left refused (#4883).
            if (!double.IsFinite(value))
                throw new NotImplementedException("Non-finite depreciation arguments are not measured (#4883)");
            var bits = BitConverter.DoubleToUInt64Bits(value);
            var biased = (int)((bits >> 52) & 0x7FF);
            BigInteger mantissa = bits & 0xF_FFFF_FFFF_FFFF;
            if (biased != 0) mantissa += BigInteger.One << 52;
            if ((bits >> 63) != 0) mantissa = -mantissa;
            var exponent = biased == 0 ? -1074 : biased - 1075;
            return exponent >= 0 ? new(mantissa << exponent, BigInteger.One)
                : new(mantissa, BigInteger.One << -exponent);
        }

        public static DepreciationNumber operator +(DepreciationNumber a, DepreciationNumber b)
            => new(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
        public static DepreciationNumber operator -(DepreciationNumber a, DepreciationNumber b)
            => new(a.numerator * b.denominator - b.numerator * a.denominator, a.denominator * b.denominator);
        public static DepreciationNumber operator *(DepreciationNumber a, DepreciationNumber b)
            => new(a.numerator * b.numerator, a.denominator * b.denominator);
        public static DepreciationNumber operator /(DepreciationNumber a, DepreciationNumber b)
            => new(a.numerator * b.denominator, a.denominator * b.numerator);

        public double ToDouble()
        {
            if (numerator.IsZero) return 0;
            var magnitude = BigInteger.Abs(numerator);
            var exponent = (int)(magnitude.GetBitLength() - denominator.GetBitLength());
            if (exponent >= 0 ? magnitude < (denominator << exponent) : (magnitude << -exponent) < denominator)
                exponent--;
            var shift = Math.Max(exponent - 52, -1074);
            var scaledNumerator = shift < 0 ? magnitude << -shift : magnitude;
            var scaledDenominator = shift > 0 ? denominator << shift : denominator;
            var significand = BigInteger.DivRem(scaledNumerator, scaledDenominator, out var remainder);
            var comparison = (remainder * 2).CompareTo(scaledDenominator);
            if (comparison > 0 || (comparison == 0 && !significand.IsEven)) significand++;
            var result = Math.ScaleB((double)significand, shift);
            return numerator.Sign < 0 ? -result : result;
        }
    }
}
