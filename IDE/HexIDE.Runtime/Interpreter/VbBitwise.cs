using System;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The operand and result rules for VB6's bitwise operators — <c>And</c>, <c>Or</c>, <c>Xor</c>,
/// <c>Eqv</c>, <c>Imp</c> and <c>Not</c>.
///
/// Separate from the general <c>TryUnpack</c> because bitwise typing is NOT arithmetic typing, and the two
/// disagree in a way that is easy to miss: arithmetic promotes <c>Byte</c> to <c>Integer</c>, while a
/// bitwise operation on two Bytes stays a Byte and complements at eight bits. <c>Not CByte(5)</c> is
/// <b>250</b>, not -6.
///
/// Every rule here is measured against vb6.exe — see "Bitwise And / Or / Xor / Not — the result-type
/// ladder" in docs/vb6-fidelity-oracle.md. None of it is inferred, because most of it is not guessable.
/// </summary>
internal static class VbBitwise
{
    /// <summary>
    /// The width a bitwise result is reported at. Ordered, because combining two operands takes the wider
    /// — with two exceptions that <see cref="Combine"/> spells out.
    /// </summary>
    internal enum Width
    {
        Boolean = 0,
        Byte    = 1,
        Integer = 2,
        Long    = 3,
    }

    /// <summary>
    /// Reduce a value to the 32 bits VB6 operates on, plus the width it will be reported at.
    ///
    /// A floating operand is rounded to Long first, half-to-even — <c>CDbl(2.5) And 3</c> is 2 and
    /// <c>CDbl(3.5) And 3</c> is 0, which is only consistent with rounding 3.5 up to 4. That is .NET's
    /// default <see cref="Math.Round(double)"/>, so it costs nothing to match.
    /// </summary>
    internal static bool TryUnpack(Vb6Value value, out long bits, out Width width)
    {
        bits = 0;
        width = Width.Long;

        if (value.Type == Vb6Value.ValueType.EmptyVariant)
        {
            // Measured: `Empty And 5` is 0 and `Empty Or 5` is 5, both Integer, and `Not Empty` is -1.
            // So Empty is an Integer zero here — not a width of its own, and not a type mismatch.
            bits = 0;
            width = Width.Integer;
            return true;
        }
        if (value.Type == Vb6Value.ValueType.Boolean)
        {
            // True is -1, which is why `True And 2` is 2 rather than True: these operators never become
            // logical short-circuits, they are always bit operations on -1 and 0.
            bits = (bool)value.Value! ? -1L : 0L;
            width = Width.Boolean;
            return true;
        }
        if (value.Type == Vb6Value.ValueType.Byte)
        {
            bits = (byte)value.Value!;
            width = Width.Byte;
            return true;
        }
        if (value.Type == Vb6Value.ValueType.Integer)
        {
            bits = (int)value.Value!;
            width = Width.Integer;
            return true;
        }
        if (value.Type == Vb6Value.ValueType.Long)
        {
            bits = (long)value.Value!;
            width = Width.Long;
            return true;
        }
        if (value.Type == Vb6Value.ValueType.Double || value.Type == Vb6Value.ValueType.Single)
        {
            var d = value.Type == Vb6Value.ValueType.Double ? (double)value.Value! : (float)value.Value!;
            if (double.IsNaN(d) || double.IsInfinity(d) || d < int.MinValue || d > int.MaxValue)
                return false;
            bits = (long)Math.Round(d, MidpointRounding.ToEven);
            width = Width.Long;
            return true;
        }
        if (value.Type == Vb6Value.ValueType.Currency || value.Type == Vb6Value.ValueType.Decimal)
        {
            var m = (decimal)value.Value!;
            if (m < int.MinValue || m > int.MaxValue)
                return false;
            bits = (long)Math.Round(m, MidpointRounding.ToEven);
            width = Width.Long;
            return true;
        }
        if (value.Type == Vb6Value.ValueType.String)
        {
            // A numeric string reports LONG, not Integer: `"12" And 10` is 8 as a Long.
            if (long.TryParse((string?)value.Value ?? "", out var parsed)
                && parsed >= int.MinValue && parsed <= int.MaxValue)
            {
                bits = parsed;
                width = Width.Long;
                return true;
            }
            return false;
        }

        return false;
    }

    /// <summary>
    /// The width a two-operand result is reported at.
    ///
    /// The wider of the two, floored at Integer — with the two exceptions that make a plain "take the
    /// larger" rule wrong: two Booleans stay Boolean, and two Bytes stay Byte. A Byte beside a Boolean is
    /// neither, and reports Integer.
    /// </summary>
    internal static Width Combine(Width left, Width right)
    {
        if (left == Width.Boolean && right == Width.Boolean) return Width.Boolean;
        if (left == Width.Byte && right == Width.Byte) return Width.Byte;
        var wider = left > right ? left : right;
        return wider < Width.Integer ? Width.Integer : wider;
    }

    /// <summary>Report a result at the given width, truncating to it as VB6 does.</summary>
    internal static Vb6Value Pack(long bits, Width width) => width switch
    {
        Width.Boolean => (Vb6Value)(bits != 0),
        Width.Byte    => new Vb6Value((byte)(bits & 0xFF)),
        Width.Integer => (Vb6Value)(int)(short)(bits & 0xFFFF),
        _             => new Vb6Value((long)(int)bits),
    };

    /// <summary>
    /// <c>Not</c> keeps its operand's own width rather than promoting — which is why
    /// <c>Not CByte(5)</c> is 250 (eight-bit complement) and not -6.
    /// </summary>
    internal static Vb6Value Not(long bits, Width width) => Pack(~bits, width);
}
