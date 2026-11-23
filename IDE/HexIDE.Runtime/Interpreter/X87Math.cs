using System;
using System.Globalization;
using System.Numerics;

namespace HexIDE.Runtime.Interpreter;

// #5259, oracle PRs #5258 and #5294. The reduced trig series and Atn use 256 fractional bits,
// leaving room for cancellation near a multiple of pi/2 and for the two rounds on store.
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
    internal static double Sin(double x) =>
        Math.Abs(x) < TinyArgument ? x : StoreDouble(Trig(x).Sin);

    internal static double Cos(double x) =>
        Math.Abs(x) < TinyArgument ? 1.0 : StoreDouble(Trig(x).Cos);

    internal static double Tan(double x)
    {
        if (Math.Abs(x) < TinyArgument)
            return x;

        var (sin, cos) = Trig(x);
        return StoreDouble(Divide(sin, cos));
    }

    internal static double Atan(double x)
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
