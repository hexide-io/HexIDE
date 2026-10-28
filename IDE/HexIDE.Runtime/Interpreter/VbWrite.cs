using System.Globalization;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What <c>Write #</c> writes for each value (#1521). Measured on vb6.exe, native and p-code agreeing, and the same under
/// de-DE as under en-GB (the oracle's "Write # and Input #"):
/// <list type="bullet">
/// <item>A String in double quotes, with a quote inside it doubled. <c>Input #</c> does not read a doubled quote back.</item>
/// <item>A number with no leading zero (<c>.5</c>, <c>-.25</c>) and a point whatever the locale. A Double is written fixed
/// while that takes at most 15 digits, and a Single at most 7, counting a fraction's leading zeros
/// (<c>.000000000000001</c>, then <c>1E-16</c>), else as <c>1.5E-20</c>. Currency and Decimal are always fixed.</item>
/// <item><c>#TRUE#</c>, <c>#FALSE#</c>, <c>#NULL#</c>, a Date as <c>#2003-01-02#</c> or <c>#2003-01-02 13:45:00#</c>, never
/// a time alone, and Empty as nothing at all.</item>
/// </list>
/// </summary>
internal static class VbWrite
{
    public static string ItemText(Vb6Value value)
    {
        if (value.Value is VBArray || VbInput.IsOneOf(value.Type, Vb6Value.ValueType.Object, Vb6Value.ValueType.UserDefinedType,
                Vb6Value.ValueType.Control, Vb6Value.ValueType.CSharpProxyObject))
            throw new NotImplementedException($"Write # of {value.Type} is not implemented (#1521)");
        return value.Value switch
        {
            _ when value.Type == Vb6Value.ValueType.Error => "#ERROR " + ((ushort)value.Value!).ToString(CultureInfo.InvariantCulture) + "#",
            _ when value.IsNull => "#NULL#",
            null => "",
            string s => "\"" + s.Replace("\"", "\"\"") + "\"",
            bool b => b ? "#TRUE#" : "#FALSE#",
            VbDate date => Date(date.Instant),
            float f => Floating(f.ToString("E6", CultureInfo.InvariantCulture), 7),
            double d => Floating(d.ToString("E14", CultureInfo.InvariantCulture), 15),
            decimal m => NoLeadingZero(m.ToString("0.############################", CultureInfo.InvariantCulture)),
            byte or short or int or long => Convert.ToString(value.Value, CultureInfo.InvariantCulture)!,
            _ => throw new NotImplementedException($"Write # of {value.Type} is not implemented (#1521)"),
        };
    }

    // `scientific` is the value to `precision` significant digits, as "d.dddE+xxx". Fixed while the fixed form needs at most
    // `precision` digits: the whole part's, or the fraction's with its leading zeros.
    // The instant, rounded to the second as VB6 shows it: 1999-12-31 23:59:59.6 is #2000-01-01# (measured, #783).
    private static string Date(DateTime d) =>
        "#" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        + (d.TimeOfDay == TimeSpan.Zero ? "" : d.ToString(" HH:mm:ss", CultureInfo.InvariantCulture)) + "#";

    private static string Floating(string scientific, int precision)
    {
        var sign = scientific.StartsWith('-') ? "-" : "";
        var body = sign.Length > 0 ? scientific[1..] : scientific;
        var e = body.IndexOf('E');
        var digits = (body[0] + body[2..e]).TrimEnd('0');
        var exponent = int.Parse(body[(e + 1)..], CultureInfo.InvariantCulture);
        if (digits.Length == 0)
            return "0";
        if (exponent >= 0 && exponent < precision)
        {
            var whole = digits.Length > exponent + 1 ? digits[..(exponent + 1)] : digits.PadRight(exponent + 1, '0');
            var fraction = digits.Length > exponent + 1 ? digits[(exponent + 1)..] : "";
            return sign + whole + (fraction.Length > 0 ? "." + fraction : "");
        }
        if (exponent < 0 && -exponent - 1 + digits.Length <= precision)
            return sign + "." + new string('0', -exponent - 1) + digits;
        var mantissa = digits.Length > 1 ? digits[0] + "." + digits[1..] : digits;
        return sign + mantissa + "E" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string NoLeadingZero(string text) =>
        text.StartsWith("0.") ? text[1..] : text.StartsWith("-0.") ? "-" + text[2..] : text;
}
