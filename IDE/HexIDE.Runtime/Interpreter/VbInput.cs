using System.Globalization;
using Antlr4.Runtime;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What a field <c>Input #</c> read becomes in the variable it reads into (#1521). Measured on vb6.exe, native and p-code
/// agreeing, and the same under de-DE as under en-GB: a number's point is a point whatever the locale (the oracle's
/// "Write # and Input #").
/// </summary>
internal static class VbInput
{
    /// <summary>How a variable of type <paramref name="target"/>, or a Variant when null, has its field read.</summary>
    public static InputReading ReadingFor(VT? target) =>
        target == VT.String ? InputReading.Text : IsNumeric(target) ? InputReading.Number : InputReading.ByFirstCharacter;

    /// <summary>The types <c>Input #</c> reads into, a Variant being null.</summary>
    public static bool Reads(VT? target) => target is null || target == VT.String || target == VT.Boolean || target == VT.Date
                                            || IsNumeric(target);

    private static bool IsNumeric(VT? t) => IsOneOf(t, VT.Byte, VT.Integer, VT.Long, VT.Single, VT.Double, VT.Currency, VT.Decimal);

    internal static bool IsOneOf(VT? t, params VT[] types) => t is not null && Array.IndexOf(types, t) >= 0;

    /// <summary>
    /// A Variant's value:
    /// <list type="bullet">
    /// <item>A quoted field or text is a String, and nothing is Empty.</item>
    /// <item><c>#TRUE#</c>, <c>#FALSE#</c> and <c>#NULL#</c> are themselves, and a date between <c>#</c>s is a Date, read
    /// month first as a date literal is (<c>#1/2/2003#</c> is 2 January). Any other <c>#</c> form is the String as
    /// written.</item>
    /// <item>A number is typed by what it needs, as <see cref="ParseNumber"/> says. One with no digits, or too large for
    /// a Double, is the String as written: <c>-abc</c>, <c>.</c>, <c>1E+400</c>.</item>
    /// </list>
    /// </summary>
    public static Vb6Value ToVariant(InputField field) => field.Kind switch
    {
        InputFieldKind.Empty => Vb6Value.Variant,
        InputFieldKind.Hash => HashForm(field.Text),
        InputFieldKind.Number => ParseNumber(field.Text, out var overflow) is { } n ? n : new Vb6Value(field.Text),
        _ => new Vb6Value(field.Text),
    };

    /// <summary>
    /// A typed variable's value. 6 and 13 leave the variable as it was, as vb6.exe does.
    /// <list type="bullet">
    /// <item>A String takes the field as written, a quoted one without its quotes.</item>
    /// <item>A number takes the number at the start of the field, rounded and range-checked as a Let would, so 2.5 is an
    /// Integer 2 and 70000 is 6. A field with no number at its start is 0, without an error: <c>abc</c>, <c>"5"</c>,
    /// <c>#TRUE#</c>, an empty field.</item>
    /// <item>A Boolean and a Date take the field as a Variant would, then: a String or Empty is False, or the zero date; a
    /// number is 6; <c>#TRUE#</c> and <c>#FALSE#</c> are a Boolean's, a date a Date's, and either in the other, or
    /// <c>#NULL#</c> in either, is 13.</item>
    /// </list>
    /// </summary>
    public static Vb6Value ToTyped(InputField field, VT target, ParserRuleContext context)
    {
        if (target == VT.String)
            return new Vb6Value(field.Text);
        if (IsNumeric(target))
        {
            if (field.Kind == InputFieldKind.Empty)
                return VbNumeric.CoerceOnStore(new Vb6Value(0), target, context);
            var n = ParseNumber(field.Text, out var overflow);
            if (overflow)
                throw new VBRunTimeException(context, VBStandardError.Overflow);
            return VbNumeric.CoerceOnStore(n ?? new Vb6Value(0), target, context);
        }
        if (target == VT.Boolean || target == VT.Date)
        {
            if (field.Kind == InputFieldKind.Number && ParseNumber(field.Text, out var overflow) is null && overflow)
                throw new NotImplementedException($"Input # of {field.Text}, too large for a Double, into a {target} is not implemented (#1521)");
            var value = ToVariant(field);
            if (value.Type == VT.String || value.Type == VT.EmptyVariant)
                return target == VT.Boolean ? new Vb6Value(false) : new Vb6Value(new VbDate(0));
            if (value.Type == target)
                return value;
            if (IsOneOf(value.Type, VT.Boolean, VT.Date, VT.Null))
                throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
            throw new VBRunTimeException(context, VBStandardError.Overflow);
        }
        throw new NotImplementedException($"Input # into a {target} is not implemented (#1521)");
    }

    private static Vb6Value HashForm(string text)
    {
        if (text.Length < 2 || !text.EndsWith('#'))
            return new Vb6Value(text);
        var inner = text[1..^1];
        switch (inner)
        {
            case "TRUE":
                return new Vb6Value(true);
            case "FALSE":
                return new Vb6Value(false);
            case "NULL":
                return Vb6Value.Null;
        }
        // HexIDE has no Error values, which CVErr makes; and how VB6 reads `#true#` in another case is unmeasured.
        if (inner.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
            || inner.ToUpperInvariant() is "TRUE" or "FALSE" or "NULL")
            throw new NotImplementedException($"Input # of {text} into a Variant is not implemented (#1521)");
        if (DateTime.TryParseExact(inner, MeasuredDateShapes, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out var date))
            return new Vb6Value(VbDate.FromDateTime(VB6BuiltIns.OnZeroDateIfTimeOnly(date)));
        // Only the shapes above were measured, and `#abc#` reads as written. .NET would take `#1/2#` as this year's 2 January,
        // and `#5 PM#` and `#Jan 5#` too, which nothing measured says VB6 does.
        if (DateTime.TryParse(inner, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out _))
            throw new NotImplementedException($"Input # of {text} into a Variant is not implemented: only #yyyy-mm-dd#, #yyyy-mm-dd hh:mm:ss#, #hh:mm:ss# and #m/d/yyyy# were measured (#1521)");
        return new Vb6Value(text);
    }

    // The dates measured between #s: what Write # writes, and a date literal's month-first form.
    private static readonly string[] MeasuredDateShapes = ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "HH:mm:ss", "M/d/yyyy"];

    /// <summary>
    /// The number at the start of <paramref name="text"/>, typed as <c>Input #</c> types it for a Variant, or null when it
    /// starts with none. What follows the number is ignored: <c>12abc</c> is 12, and <c>5%</c> is 5.
    /// <list type="bullet">
    /// <item>A whole number is an Integer, a Long, a Double up to 15 digits, a Decimal up to its 29, and a Double
    /// beyond.</item>
    /// <item>A fraction with at most 4 places, its trailing zeros dropped, is a Currency where it fits one, so 1.50 is
    /// Currency and 1.00000 an Integer. With more places it is a Decimal, rounded to 29 digits, and a Double where that
    /// would still need more than 28 places.</item>
    /// <item>Zero written with places is a Currency up to 4 of them and a Decimal beyond: <c>0.0000</c>, then
    /// <c>-0.00000</c>.</item>
    /// <item>An exponent, written with E or D, makes a Double; <paramref name="overflow"/> is set where it is too large
    /// for one.</item>
    /// <item><c>&amp;H</c> and <c>&amp;O</c> read as 32 bits, higher ones dropped, and as 16 bits where the value fits in
    /// them: <c>&amp;HFFFF</c> is an Integer -1, <c>&amp;H10000</c> a Long, and <c>&amp;HFFFFFFFF</c> an Integer -1
    /// again. A sign before the <c>&amp;</c> makes no number.</item>
    /// </list>
    /// </summary>
    public static Vb6Value? ParseNumber(string text, out bool overflow)
    {
        overflow = false;
        if (text.Length > 1 && text[0] == '&')
            return Radix(text);
        var i = 0;
        var negative = false;
        if (i < text.Length && text[i] is '+' or '-')
        {
            negative = text[i] == '-';
            i++;
        }
        var wholeStart = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
            i++;
        var whole = text[wholeStart..i];
        var fraction = "";
        if (i < text.Length && text[i] == '.')
        {
            var fractionStart = ++i;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
                i++;
            fraction = text[fractionStart..i];
        }
        if (whole.Length == 0 && fraction.Length == 0)
            return null;
        if (i < text.Length && text[i] is 'E' or 'e' or 'D' or 'd')
        {
            var j = i + 1;
            if (j < text.Length && text[j] is '+' or '-')
                j++;
            var exponentStart = j;
            while (j < text.Length && char.IsAsciiDigit(text[j]))
                j++;
            if (j > exponentStart)
            {
                var mantissa = (whole.Length == 0 ? "0" : whole) + (fraction.Length > 0 ? "." + fraction : "");
                var d = double.Parse(mantissa + "E" + text[(i + 1)..j], CultureInfo.InvariantCulture);
                if (double.IsInfinity(d))
                {
                    overflow = true;
                    return null;
                }
                return new Vb6Value(negative ? -d : d);
            }
        }
        var places = fraction.TrimEnd('0');
        whole = whole.TrimStart('0');
        if (whole.Length == 0 && places.Length == 0)
        {
            // Zero, which keeps the places it was written with.
            return fraction.Length == 0 ? new Vb6Value(0)
                : fraction.Length <= 4 ? Vb6Value.NewCurrency(0m) : Vb6Value.NewDecimal(0m);
        }
        var sign = negative ? "-" : "";
        if (places.Length == 0)
        {
            if (whole.Length <= 9 || (whole.Length == 10 && long.Parse(whole, CultureInfo.InvariantCulture) <= int.MaxValue + (negative ? 1L : 0L)))
            {
                var n = long.Parse(sign + whole, CultureInfo.InvariantCulture);
                return n is >= short.MinValue and <= short.MaxValue ? new Vb6Value((int)n) : new Vb6Value(n);
            }
            if (whole.Length <= 15)
                return new Vb6Value(double.Parse(sign + whole, CultureInfo.InvariantCulture));
            if (whole.Length <= 29 && decimal.TryParse(sign + whole, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big))
                return Vb6Value.NewDecimal(big);
            return new Vb6Value(double.Parse(sign + whole, CultureInfo.InvariantCulture));
        }
        var written = sign + (whole.Length == 0 ? "0" : whole) + "." + places;
        var value = decimal.TryParse(written, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var m)
            ? m : (decimal?)null;
        if (places.Length <= 4 && value is { } c && Math.Abs(c) <= 922337203685477.5807m)
            return Vb6Value.NewCurrency(c);
        var significant = (whole + places).TrimStart('0').Length;
        var scale = places.Length - Math.Max(0, significant - 29);
        if (whole.Length <= 29 && scale <= 28 && value is { } dec)
            return Vb6Value.NewDecimal(dec);
        if (whole.Length > 29 || scale > 28)
            return new Vb6Value(double.Parse(written, CultureInfo.InvariantCulture));
        throw new NotImplementedException($"Input # of {text} is not implemented: a Decimal cannot hold it (#1521)");
    }

    private static Vb6Value? Radix(string text)
    {
        var radix = char.ToUpperInvariant(text[1]) switch { 'H' => 16, 'O' => 8, _ => 0 };
        if (radix == 0)
            return null;
        var i = 2;
        ulong value = 0;
        var any = false;
        while (i < text.Length && Uri.IsHexDigit(text[i]) && Convert.ToInt32(text[i].ToString(), 16) < radix)
        {
            value = ((value * (ulong)radix) + (ulong)Convert.ToInt32(text[i].ToString(), 16)) & 0xFFFFFFFF;
            any = true;
            i++;
        }
        if (!any)
            return null;
        if (value <= 0xFFFF)
            return new Vb6Value((int)(short)(ushort)value);
        var n = (int)(uint)value;
        return new Vb6Value(n);
    }
}
