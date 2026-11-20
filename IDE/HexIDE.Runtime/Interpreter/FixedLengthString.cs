using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

// Store-time rules measured in #179 (vb6.exe SP6, native and p-code; oracle PR #4019).
internal static class FixedLengthString
{
    internal static int Width(VB6Parser.FieldLengthContext length, ExecutionEnvironment env, ExecutionState state)
    {
        Vb6Value value;
        if (length.INTEGERLITERAL() is { } literal)
            value = new Vb6Value(long.Parse(literal.GetText(), System.Globalization.CultureInfo.InvariantCulture));
        else
        {
            if (!env.TryGetVariableLocation(length.ambiguousIdentifier().GetText(), out var location) || !state.IsConstant(location))
                throw new VBCompileErrorException("Constant expression required");
            value = state[location];
        }
        // #179 / oracle PR #4923: numeric constants (including numeric text and Date serials)
        // round half to even. Non-numeric text has no measured compile error.
        double number;
        if (value.Type == Vb6Value.ValueType.Boolean)
            number = (bool)value.Value! ? -1 : 0;
        else if (value.Type == Vb6Value.ValueType.String)
        {
            try { number = VB6BuiltIns.ToNum(value); }
            catch (VBRunTimeException)
            {
                throw new NotImplementedException("Fixed-length string width conversion is not implemented (#179)");
            }
        }
        else if (!Vb6Value.TryNumericToDouble(value, out number))
            throw new NotImplementedException("Fixed-length string width conversion is not implemented (#179)");
        var count = Math.Round(number, MidpointRounding.ToEven);
        if (count < 1 || count > 65526)
            throw new VBCompileErrorException("Invalid length for fixed-length string");
        return (int)count;
    }

    internal static VBArray Array(int width, IReadOnlyList<(int, int)>? bounds)
    {
        var array = bounds is null ? VBArray.Unallocated(Vb6Value.ValueType.String, () => Initial(width))
            : new VBArray(Vb6Value.ValueType.String, bounds, () => Initial(width));
        array.FixedStringWidth = width;
        array.IsDynamic = bounds is null;
        return array;
    }

    internal static Vb6Value Initial(int width) => new(new string('\0', width));

    internal static Vb6Value Store(Vb6Value value, int width, ParserRuleContext? context = null)
    {
        var text = (string)VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, context).WithoutOddBytes("FixedLengthString text conversion").Value!;
        return new Vb6Value(text.Length > width ? text[..width] : text.PadRight(width));
    }
}
