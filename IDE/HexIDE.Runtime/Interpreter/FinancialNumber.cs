using System;
using System.Numerics;

namespace HexIDE.Runtime.Interpreter;

// Exact rational intermediates, with 160-bit square roots for fractional powers. Financial results
// cannot use Double arithmetic throughout: #2523 differs by a bit even for an ordinary loan payment.
internal readonly struct FinancialNumber
{
    private readonly BigInteger numerator;
    private readonly BigInteger denominator;

    private FinancialNumber(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero) throw new NotImplementedException("A singular financial calculation is unmeasured (#2523)");
        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        this.numerator = numerator / gcd * denominator.Sign;
        this.denominator = BigInteger.Abs(denominator / gcd);
    }

    public static implicit operator FinancialNumber(int value) => new(value, BigInteger.One);

    public static FinancialNumber FromDouble(double value)
    {
        if (!double.IsFinite(value)) throw new NotImplementedException("Non-finite financial arguments are unmeasured (#2523)");
        var bits = BitConverter.DoubleToUInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7ff);
        BigInteger mantissa = bits & 0xfffffffffffff;
        if (exponent != 0) mantissa += BigInteger.One << 52;
        if ((bits >> 63) != 0) mantissa = -mantissa;
        var shift = exponent == 0 ? -1074 : exponent - 1075;
        return shift >= 0 ? new(mantissa << shift, BigInteger.One) : new(mantissa, BigInteger.One << -shift);
    }

    public static FinancialNumber operator +(FinancialNumber a, FinancialNumber b) =>
        new(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
    public static FinancialNumber operator -(FinancialNumber a) => new(-a.numerator, a.denominator);
    public static FinancialNumber operator -(FinancialNumber a, FinancialNumber b) => a + -b;
    public static FinancialNumber operator *(FinancialNumber a, FinancialNumber b) =>
        new(a.numerator * b.numerator, a.denominator * b.denominator);
    public static FinancialNumber operator /(FinancialNumber a, FinancialNumber b) =>
        new(a.numerator * b.denominator, a.denominator * b.numerator);

    public double ToDouble()
    {
        if (numerator.IsZero) return 0;
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

    public FinancialNumber Pow(double periods, bool signedIntegerBase = false)
    {
        // Bound work and refuse unmeasured extreme domains instead of inventing their VB6 errors.
        if ((numerator.Sign <= 0 && !(signedIntegerBase && periods == Math.Truncate(periods))) || !double.IsFinite(periods) || Math.Abs(periods) > 10000)
            throw new NotImplementedException("This financial power domain is unmeasured (#2523)");
        if (periods < 0) return 1 / Pow(-periods, signedIntegerBase);
        int whole = (int)periods;
        FinancialNumber result = new(BigInteger.Pow(numerator, whole), BigInteger.Pow(denominator, whole));
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
        return result;
    }

    private FinancialNumber RoundPrecision()
    {
        if (numerator.IsZero) return this;
        int shift = 159 - BinaryExponent(BigInteger.Abs(numerator), denominator);
        return shift >= 0
            ? new(RoundQuotient(numerator << shift, denominator), BigInteger.One << shift)
            : new(RoundQuotient(numerator, denominator << -shift) << -shift, BigInteger.One);
    }

    public FinancialNumber Log()
    {
        if (numerator.Sign <= 0)
            throw new NotImplementedException("This financial logarithm domain is unmeasured (#2523)");
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
