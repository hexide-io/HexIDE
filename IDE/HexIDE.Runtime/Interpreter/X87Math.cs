using System;
using System.Globalization;
using System.Numerics;

namespace HexIDE.Runtime.Interpreter;

// #5259, oracle PRs #5258 and #5294. The full model keeps 256 fractional bits for
// cancellation and the two rounds on store. #5337 retains it near rounding boundaries.
internal static class X87Math
{
    internal const double ArgumentLimit = 9223372036854775808.0; // 2^63
    private const double TinyArgument = 1.0 / (1L << 33);
    private const int FractionBits = 256;
    private static readonly BigInteger One = BigInteger.One << FractionBits;

    // The trig reduction uses the x87 constant. Atn uses pi itself.
    private static readonly BigInteger HalfPi66 =
        BigInteger.Parse("3243F6A8885A308D3", NumberStyles.AllowHexSpecifier) << (FractionBits - 65);
    private static readonly BigInteger Pi = BigInteger.Parse(
        "3243F6A8885A308D313198A2E03707344A4093822299F31D0082EFA98EC4E6C89",
        NumberStyles.AllowHexSpecifier);

    // Below this bound the cubic correction is less than half an x87 ulp. Keep the input
    // directly, including subnormal values and signed zero.
    internal static double Sin(double x) => Evaluate(x, 0);
    internal static double Cos(double x) => Evaluate(x, 1);
    internal static double Tan(double x) => Evaluate(x, 2);
    internal static double Atan(double x) => Evaluate(x, 3);

    private static double Evaluate(double x, int function)
    {
        if (Math.Abs(x) < TinyArgument)
            return function == 1 ? 1 : x;

        DoubleDouble value;
        if (function == 3)
            value = FastAtan(Math.Abs(x));
        else
        {
            // Keep the cancellation exact, including the largest admitted arguments.
            BigInteger argument = ToFixed(Math.Abs(x));
            BigInteger turns = RoundQuotient(argument, HalfPi66);
            DoubleDouble remainder = FromFixed(argument - turns * HalfPi66);
            DoubleDouble square = -(remainder * remainder);
            DoubleDouble sin = SinCoefficients[^1], cos = CosCoefficients[^1];
            for (int i = SinCoefficients.Length - 2; i >= 0; --i)
            {
                sin = sin * square + SinCoefficients[i];
                cos = cos * square + CosCoefficients[i];
            }
            sin *= remainder;
            (sin, cos) = (int)(turns & 3) switch
            {
                0 => (sin, cos),
                1 => (cos, -sin),
                2 => (-sin, -cos),
                _ => (-cos, sin)
            };
            if (x < 0)
                sin = -sin;
            value = function == 0 ? sin : function == 1 ? cos : sin / cos;
        }

        if (TryStoreFast(value.Hi, value.Lo, out double result))
            return function == 3 ? Math.CopySign(result, x) : result;

        return function switch
        {
            0 => SinExact(x),
            1 => CosExact(x),
            2 => TanExact(x),
            _ => AtanExact(x)
        };
    }

    // The series error is bounded by 2^-88 times the magnitude; see the derivation in
    // scripts/probes/issue-5337/README.md. On this scale that is at most 2^-24.
    internal static bool TryStoreFast(double hi, double lo, out double result)
    {
        double magnitude = Math.Abs(hi);
        double low = hi < 0 ? -lo : lo;
        int exponent = Math.ILogB(magnitude);
        // A negative tail below an exact power of two belongs to the lower binade.
        if ((BitConverter.DoubleToUInt64Bits(magnitude) & 0x000FFFFFFFFFFFFFUL) == 0 && low < 0)
            --exponent;
        double high = Math.ScaleB(magnitude, 63 - exponent);
        low = Math.ScaleB(low, 63 - exponent);
        double rounded = Math.Round(low);
        result = 0;
        if (!double.IsFinite(high) || Math.Abs(Math.Abs(low - rounded) - 0.5) <= Math.ScaleB(1.0, -24))
            return false;

        // high is an integer on this scale. Once the 64-bit round is certified,
        // this addition performs the 53-bit round, including its exact halfway cases.
        result = Math.CopySign(Math.ScaleB(high + rounded, exponent - 63), hi);
        return true;
    }

    private static readonly DoubleDouble[] SinCoefficients = TrigCoefficients(true);
    private static readonly DoubleDouble[] CosCoefficients = TrigCoefficients(false);
    private static readonly DoubleDouble[] AtanCoefficients = CreateAtanCoefficients();
    private static readonly DoubleDouble QuarterPi = FromFixed(Pi >> 2);

    private static DoubleDouble[] TrigCoefficients(bool sine)
    {
        var coefficients = new DoubleDouble[16];
        BigInteger factorial = BigInteger.One;
        for (int n = 0; n < coefficients.Length; ++n)
        {
            if (n > 0)
                factorial *= (2 * n) * (sine ? 2 * n + 1 : 2 * n - 1);
            coefficients[n] = FromFixed(One / factorial);
        }
        return coefficients;
    }

    private static DoubleDouble[] CreateAtanCoefficients()
    {
        var coefficients = new DoubleDouble[49];
        for (int n = 0; n < coefficients.Length; ++n)
            coefficients[n] = FromFixed(One / (2 * n + 1));
        return coefficients;
    }

    private static DoubleDouble FastAtan(double x)
    {
        bool reciprocal = x > 1;
        DoubleDouble value = new(x, 0);
        if (reciprocal)
            value = new DoubleDouble(1, 0) / value;
        bool nearOne = value.Hi > 0.5;
        if (nearOne)
            value = (value + new DoubleDouble(-1, 0)) / (value + new DoubleDouble(1, 0));
        DoubleDouble square = -(value * value);
        // Shorter series retain the same tail bound on these dyadic intervals.
        double magnitude = Math.BitIncrement(Math.Abs(value.Hi));
        int last = magnitude <= 1.0 / 64 ? 9 : magnitude <= 1.0 / 16 ? 15 : magnitude <= 1.0 / 4 ? 31 : 48;
        DoubleDouble sum = AtanCoefficients[last];
        for (int n = last - 1; n >= 0; --n)
            sum = sum * square + AtanCoefficients[n];
        sum *= value;
        if (nearOne)
            sum += QuarterPi;
        if (reciprocal)
            sum = QuarterPi + QuarterPi + -sum;
        return sum;
    }

    private static DoubleDouble FromFixed(BigInteger value)
    {
        double high = Math.ScaleB((double)value, -FractionBits);
        BigInteger highFixed = ToFixed(Math.Abs(high));
        if (high < 0)
            highFixed = -highFixed;
        return new(high, Math.ScaleB((double)(value - highFixed), -FractionBits));
    }

    private readonly record struct DoubleDouble(double Hi, double Lo)
    {
        public static DoubleDouble operator -(DoubleDouble x) => new(-x.Hi, -x.Lo);

        public static DoubleDouble operator +(DoubleDouble x, DoubleDouble y)
        {
            double sum = x.Hi + y.Hi;
            double part = sum - x.Hi;
            double error = (x.Hi - (sum - part)) + (y.Hi - part) + x.Lo + y.Lo;
            double high = sum + error;
            return new(high, error - (high - sum));
        }

        public static DoubleDouble operator *(DoubleDouble x, DoubleDouble y)
        {
            double product = x.Hi * y.Hi;
            double error = Math.FusedMultiplyAdd(x.Hi, y.Hi, -product) + x.Hi * y.Lo + x.Lo * y.Hi;
            double high = product + error;
            return new(high, error - (high - product));
        }

        public static DoubleDouble operator /(DoubleDouble x, DoubleDouble y)
        {
            double quotient = x.Hi / y.Hi;
            DoubleDouble residual = x + -(y * new DoubleDouble(quotient, 0));
            return new DoubleDouble(quotient, 0) + new DoubleDouble((residual.Hi + residual.Lo) / y.Hi, 0);
        }
    }

    internal static double SinExact(double x) =>
        Math.Abs(x) < TinyArgument ? x : StoreDouble(Trig(x).Sin);

    internal static double CosExact(double x) =>
        Math.Abs(x) < TinyArgument ? 1.0 : StoreDouble(Trig(x).Cos);

    internal static double TanExact(double x)
    {
        if (Math.Abs(x) < TinyArgument)
            return x;

        var (sin, cos) = Trig(x);
        return StoreDouble(Divide(sin, cos));
    }

    internal static double AtanExact(double x)
    {
        if (Math.Abs(x) < TinyArgument)
            return x;

        BigInteger value = ToFixed(Math.Abs(x));
        bool reciprocal = value > One;
        if (reciprocal)
            value = Divide(One, value);

        // Reduce the alternating series to |value| <= 1/2.
        bool nearOne = value > (One >> 1);
        if (nearOne)
            value = Divide(value - One, value + One);

        BigInteger square = -Multiply(value, value);
        BigInteger sum = value;
        BigInteger power = value;
        for (int n = 3; !power.IsZero; n += 2)
        {
            power = Multiply(power, square);
            sum += power / n;
        }

        if (nearOne)
            sum += Pi >> 2;
        if (reciprocal)
            sum = (Pi >> 1) - sum;
        return Math.CopySign(StoreDouble(sum), x);
    }

    internal static double Sqrt(double x)
    {
        if (x == 0)
            return x;

        // Scale to [1, 4) so even the smallest subnormal has a full fixed-point significand.
        int exponent = Math.ILogB(x) & ~1;
        BigInteger radicand = ToFixed(Math.ScaleB(x, -exponent)) << FractionBits;
        BigInteger root = One << 1;
        // Integer Newton iteration from above, terminating at the floor of the root.
        while (true)
        {
            BigInteger next = (root + radicand / root) >> 1;
            if (next >= root)
                return Math.ScaleB(StoreDouble(root), exponent / 2);

            root = next;
        }
    }

    private static (BigInteger Sin, BigInteger Cos) Trig(double x)
    {
        BigInteger argument = ToFixed(Math.Abs(x));
        BigInteger turns = RoundQuotient(argument, HalfPi66);
        BigInteger remainder = argument - turns * HalfPi66;
        BigInteger square = -Multiply(remainder, remainder);
        BigInteger sin = remainder, sinTerm = remainder;
        BigInteger cos = One, cosTerm = One;
        for (int n = 2; !sinTerm.IsZero || !cosTerm.IsZero; n += 2)
        {
            sinTerm = Multiply(sinTerm, square) / (n * (n + 1));
            cosTerm = Multiply(cosTerm, square) / (n * (n - 1));
            sin += sinTerm;
            cos += cosTerm;
        }

        (sin, cos) = (int)(turns & 3) switch
        {
            0 => (sin, cos),
            1 => (cos, -sin),
            2 => (-sin, -cos),
            _ => (-cos, sin)
        };
        return (x < 0 ? -sin : sin, cos);
    }

    private static BigInteger ToFixed(double x)
    {
        // Callers supply positive, normal doubles here. Splitting the bits makes the reduction exact.
        ulong bits = BitConverter.DoubleToUInt64Bits(x);
        int exponent = (int)(bits >> 52) - 1023;
        ulong significand = (bits & 0x000FFFFFFFFFFFFFUL) | (1UL << 52);
        return (BigInteger)significand << (FractionBits + exponent - 52);
    }

    private static BigInteger Multiply(BigInteger x, BigInteger y) => (x * y) / One;

    private static BigInteger Divide(BigInteger x, BigInteger y) => (x << FractionBits) / y;

    private static BigInteger RoundQuotient(BigInteger numerator, BigInteger denominator)
    {
        BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
        int half = (remainder << 1).CompareTo(denominator);
        return half > 0 || (half == 0 && !quotient.IsEven) ? quotient + 1 : quotient;
    }

    private static double StoreDouble(BigInteger fixedPoint)
    {
        BigInteger magnitude = BigInteger.Abs(fixedPoint);
        int shift = Math.Max(0, (int)magnitude.GetBitLength() - 64);
        BigInteger extended = RoundQuotient(magnitude, BigInteger.One << shift);
        int narrowShift = Math.Max(0, (int)extended.GetBitLength() - 53);
        BigInteger stored = RoundQuotient(extended, BigInteger.One << narrowShift);
        double result = Math.ScaleB((double)stored, shift + narrowShift - FractionBits);
        return fixedPoint.Sign < 0 ? -result : result;
    }
}
