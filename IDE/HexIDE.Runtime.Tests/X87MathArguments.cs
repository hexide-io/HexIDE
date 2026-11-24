namespace HexIDE.Runtime.Tests;

// Model comparisons requested by #5337; these are not additional VB6 measurements.
internal static class X87MathArguments
{
    internal static double[] Create(int count)
    {
        var random = new Random(5337);
        var values = new double[count];
        for (int i = 0; i < count; ++i)
        {
            double value;
            if (i % 4 == 0)
            {
                int turns = i == 0 ? 1 << 20 : random.Next(1, (1 << 20) + 1);
                value = turns * (Math.PI / 2);
                int offset = random.Next(-3, 4);
                for (int j = 0; j < Math.Abs(offset); ++j)
                    value = offset < 0 ? Math.BitDecrement(value) : Math.BitIncrement(value);
            }
            else
            {
                ulong mantissa = (ulong)random.NextInt64() & 0x000FFFFFFFFFFFFFUL;
                ulong exponent = (ulong)random.Next(1023 - 33, 1023 + 63);
                value = BitConverter.UInt64BitsToDouble((exponent << 52) | mantissa);
            }
            values[i] = random.Next(2) == 0 ? value : -value;
        }
        return values;
    }
}
