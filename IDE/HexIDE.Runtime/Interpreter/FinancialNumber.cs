using System;
using System.Numerics;

namespace HexIDE.Runtime.Interpreter;

// Exact rational intermediates, with 160-bit square roots for fractional powers. Financial results
// cannot use Double arithmetic throughout: #2523 differs by a bit even for an ordinary loan payment.
internal readonly struct FinancialNumber
{
    private readonly BigInteger numerator;
    private readonly BigInteger denominator;
    private readonly bool negativeZero;
    private bool IsNegative => numerator.Sign < 0 || negativeZero;

    private FinancialNumber(BigInteger numerator, BigInteger denominator, bool negativeZero = false)
    {
        if (denominator.IsZero) throw new VBRunTimeException(numerator.IsZero ? VBStandardError.Overflow : VBStandardError.DivisionByZero);
        this.negativeZero = numerator.IsZero && negativeZero;
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        this.numerator = numerator / gcd * denominator.Sign;
        this.denominator = BigInteger.Abs(denominator / gcd);
    }

    public static implicit operator FinancialNumber(int value) => new(value, BigInteger.One);

    public static FinancialNumber FromDouble(double value)
    {
        // Non-finite inputs require API construction in VB6; deliberately left refused (#4883).
        if (!double.IsFinite(value)) throw new NotImplementedException("Non-finite financial arguments are unmeasured (#4883)");
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff);
        BigInteger mantissa = bits & 0xfffffffffffff;
        if (exponent != 0) mantissa += BigInteger.One << 52;
        if ((bits >> 63) != 0) mantissa = -mantissa;
        var shift = exponent == 0 ? -1074 : exponent - 1075;
        return shift >= 0 ? new(mantissa << shift, BigInteger.One) : new(mantissa, BigInteger.One << -shift, (bits >> 63) != 0);
    }

    public static FinancialNumber operator +(FinancialNumber a, FinancialNumber b) =>
        new(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator, a.IsNegative && b.IsNegative);
    public static FinancialNumber operator -(FinancialNumber a) => new(-a.numerator, a.denominator, !a.IsNegative);
    public static FinancialNumber operator -(FinancialNumber a, FinancialNumber b) => a + -b;
    public static FinancialNumber operator *(FinancialNumber a, FinancialNumber b) =>
        new(a.numerator * b.numerator, a.denominator * b.denominator, a.IsNegative != b.IsNegative);
    public static FinancialNumber operator /(FinancialNumber a, FinancialNumber b) =>
        new(a.numerator * b.denominator, a.denominator * b.numerator, a.IsNegative != b.IsNegative);

    public double ToDouble()
    {
        if (numerator.IsZero) return negativeZero ? -0.0 : 0.0;
        var magnitude = BigInteger.Abs(numerator);
        var exponent = BinaryExponent(magnitude, denominator);
        var shift = Math.Max(52 - exponent, -971);
        shift = Math.Min(shift, 1074);
        var rounded = shift >= 0
            ? RoundQuotient(magnitude << shift, denominator)
            : RoundQuotient(magnitude, denominator << -shift);
        return Math.CopySign(Math.ScaleB((double)rounded, -shift), numerator.Sign);
    }

    private static int BinaryExponent(BigInteger n, BigInteger d)
    {
        var exponent = checked((int)(n.GetBitLength() - d.GetBitLength()));
        if (exponent >= 0 ? n < (d << exponent) : (n << -exponent) < d) exponent--;
        return exponent;
    }

    private static BigInteger RoundQuotient(BigInteger n, BigInteger d)
    {
        var q = BigInteger.DivRem(n, d, out var remainder);
        var comparison = (remainder * 2).CompareTo(d);
        return comparison > 0 || (comparison == 0 && !q.IsEven) ? q + 1 : q;
    }

    public FinancialNumber Pow(double periods, bool signedIntegerBase = true, bool checkRange = true)
    {
        // #4883: negative integral bases run; a fractional power of a negative base is overflow.
        if ((numerator.Sign < 0 && !(signedIntegerBase && periods == Math.Truncate(periods))) || !double.IsFinite(periods))
            throw new VBRunTimeException(VBStandardError.Overflow);
        // #4883 extends the measured payment schedules to 20,000 periods, not arbitrary sizes.
        if (Math.Abs(periods) > 20000) throw new NotImplementedException("This financial power domain is unmeasured (#4883)");
        if (periods < 0)
        {
            var reciprocal = 1 / Pow(-periods, signedIntegerBase, checkRange: false);
            return checkRange ? CheckPower(reciprocal) : reciprocal;
        }
        if (numerator.IsZero) return periods == 0 ? 1 : this;
        int whole = (int)periods;
        FinancialNumber result = 1;
        if (whole <= 10000)
            result = new(BigInteger.Pow(numerator, whole), BigInteger.Pow(denominator, whole));
        else
        {
            // Bound intermediate size for long schedules, retaining far more than Double precision.
            var factor = this;
            for (int remaining = whole; remaining != 0; remaining >>= 1)
            {
                if ((remaining & 1) != 0) result = (result * factor).RoundSignificand(160);
                if (remaining > 1) factor = (factor * factor).RoundSignificand(160);
            }
        }
        double fraction = periods - whole;
        var root = this;
        while (fraction != 0)
        {
            root = root.Sqrt();
            fraction *= 2;
            if (fraction >= 1)
            {
                result = (result * root).RoundPrecision();
                fraction -= 1;
            }
        }
        return checkRange ? CheckPower(result) : result;
    }

    private static FinancialNumber CheckPower(FinancialNumber result)
    {
        double value = result.ToDouble();
        if (!double.IsFinite(value)) throw new VBRunTimeException(VBStandardError.Overflow);
        // #4883: a power that underflows to Double zero subsequently divides by zero in PV.
        return value == 0 ? FromDouble(value) : result;
    }

    private FinancialNumber RoundPrecision()
    {
        if (numerator.IsZero) return this;
        int shift = 159 - BinaryExponent(BigInteger.Abs(numerator), denominator);
        return shift >= 0
            ? new(RoundQuotient(numerator << shift, denominator), BigInteger.One << shift)
            : new(RoundQuotient(numerator, denominator << -shift) << -shift, BigInteger.One);
    }

    public FinancialNumber RoundSignificand(int precision)
    {
        if (numerator.IsZero) return this;
        var magnitude = BigInteger.Abs(numerator);
        int shift = precision - 1 - BinaryExponent(magnitude, denominator);
        return shift >= 0
            ? new(numerator.Sign * RoundQuotient(magnitude << shift, denominator), BigInteger.One << shift)
            : new((numerator.Sign * RoundQuotient(magnitude, denominator << -shift)) << -shift, BigInteger.One);
    }

    public FinancialNumber PowReciprocal(int divisor)
    {
        // #4883's negative-reinvestment example has a positive radicand; negative roots remain unmeasured.
        if (numerator.Sign < 0) throw new NotImplementedException("This MIRR power domain is unmeasured (#4883)");
        if (divisor < 1) throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        if (numerator.IsZero) return this;
        if (divisor == 1) return this;
        FinancialNumber result = 1;
        var root = this;
        int remainder = 1;
        // Expand the exact reciprocal, rather than its nearest Double (notably 1/5).
        // 180 binary places exceed the 160 retained bits and the final 64-bit rounding.
        for (int i = 0; i < 180 && remainder != 0; i++)
        {
            root = root.Sqrt();
            remainder *= 2;
            if (remainder >= divisor)
            {
                result = (result * root).RoundSignificand(160);
                remainder -= divisor;
            }
        }
        return result;
    }

    public FinancialNumber Log()
    {
        if (numerator.Sign <= 0)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        int exponent = BinaryExponent(numerator, denominator);
        var scaled = exponent >= 0 ? new FinancialNumber(numerator, denominator << exponent)
            : new FinancialNumber(numerator << -exponent, denominator);
        return LogSeries(scaled) + exponent * LogSeries(2);
    }

    private static FinancialNumber LogSeries(FinancialNumber value)
    {
        var z = ((value - 1) / (value + 1)).RoundPrecision();
        var square = (z * z).RoundPrecision();
        var term = z;
        var sum = z;
        // Range reduction bounds |z| by 1/3; 180 terms exceed the retained 160 bits.
        for (int i = 1; i < 180; i++)
        {
            term = (term * square).RoundPrecision();
            sum = (sum + term / (2 * i + 1)).RoundPrecision();
        }
        return 2 * sum;
    }

    private FinancialNumber Sqrt()
    {
        int shift = 160 - (int)Math.Floor(BinaryExponent(numerator, denominator) / 2.0);
        BigInteger square = shift >= 0 ? (numerator << (2 * shift)) / denominator
            : numerator / (denominator << (-2 * shift));
        BigInteger root = BigInteger.One << checked((int)((square.GetBitLength() + 1) / 2));
        while (true)
        {
            var next = (root + square / root) / 2;
            if (next >= root) break;
            root = next;
        }
        return shift >= 0 ? new(root, BigInteger.One << shift) : new(root << -shift, BigInteger.One);
    }
}
