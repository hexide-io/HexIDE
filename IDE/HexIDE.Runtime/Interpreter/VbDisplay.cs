using System.Globalization;
using System.Numerics;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The text VB6 makes of a value wherever it turns one into a String: <c>CStr</c>, <c>&amp;</c>, a String variable
/// or property, the string functions, <c>MsgBox</c>, <c>Format</c> with no mask (#772, #771).
/// </summary>
/// <remarks>
/// <para>
/// Every rule here was measured on vb6.exe under an en-GB locale (<c>docs/vb6-fidelity-oracle/turning-a-number-or-a-date-into-text.md</c>, "Turning a
/// number or a date into text"). .NET's own <c>ToString()</c> differs in each of them: it writes a Single or Double
/// in its shortest round-trip form (<c>0.33333334</c>, <c>0.30000000000000004</c>), never switches to E notation
/// where VB6 does, writes negative zero as <c>-0</c>, keeps a Currency's or Decimal's trailing zeros, and always
/// writes both halves of a date.
/// </para>
/// <para>
/// The locale supplies the decimal separator and the date and time patterns, as it does for VB6 and for
/// <c>Format</c> here. Everything else is VB6's own: the significant digits, where E notation starts, its spelling,
/// and which half of a date is left out. Two things the locale does NOT supply, although .NET would take them from
/// it: the sign is always an ASCII <c>-</c> (ICU gives some locales U+2212, or bidi marks, which Windows' own locale
/// data does not, and which <c>Val</c> cannot read back), and a date is always in the Gregorian calendar, VB6's
/// default (<c>Calendar = vbCalHijri</c> is a separate feature HexIDE lacks).
/// </para>
/// </remarks>
public static class VbDisplay
{
    private static readonly DateTime ZeroDate = new(1899, 12, 30);

    /// <summary>The text VB6 makes of <paramref name="value"/>, in the running program's locale (<see cref="VbLocale"/>).</summary>
    public static string ToText(Vb6Value value) => ToText(value, VbLocale.Current);

    /// <summary>
    /// As <see cref="ToText(Vb6Value)"/>, but null for a value with nothing in it: an omitted argument, or Empty. The
    /// callers tell an omitted argument from an empty string by it (MsgBox's title takes App.Title only when omitted).
    /// </summary>
    public static string? ToTextOrNull(Vb6Value value) => value.Value is null ? null : ToText(value);

    /// <summary>The text VB6 makes of <paramref name="value"/>, in <paramref name="culture"/>.</summary>
    public static string ToText(Vb6Value value, CultureInfo culture) => value.Type == Vb6Value.ValueType.Error
        ? throw new VBRunTimeException(VBStandardError.TypeMismatch)
        : value.Value switch
    {
        null => "",
        // An object stands for its default member: Err its Number, and one with none is 438 (ObjectAsValue). Its CLR
        // type name, which the last arm gave, is never right (#436).
        VbObject or ICSharpProxy => ToText(ObjectAsValue.Scalar(value, null), culture),
        VBArray => throw new VBRunTimeException(VBStandardError.TypeMismatch),
        string s => s,
        float f => Floating(f, 7, culture),
        double d => Floating(d, 15, culture),
        decimal m => Exact(m, culture),
        VbDate dt => Date(dt.Instant, culture),
        // The integer types have no separator to take from the locale, only a sign, which is always ASCII.
        byte or short or int or long => ((IFormattable)value.Value).ToString(null, CultureInfo.InvariantCulture),
        _ => value.Value.ToString() ?? "",
    };

    /// <summary>
    /// A Single (7 significant digits) or a Double (15). Rounded half away from zero: measured on a tie each way
    /// for both types, where half-to-even would have answered differently (<c>CSng(1234566.5)</c> is
    /// <c>1234567</c>, and <c>1234567890123445#</c> is <c>1.23456789012345E+15</c>).
    /// </summary>
    /// <remarks>
    /// Fixed notation unless it would need more digits than the precision: before the point, when the exponent is
    /// at least the precision (<c>1E+07</c> for a Single, <c>1E+15</c> for a Double); after it, when the leading
    /// zeros and the significant digits together exceed the precision (<c>0.0012345</c> but <c>1.2345E-04</c> for
    /// a Single). So the switch to E notation for a small number depends on how many digits it has, which no
    /// .NET format does. The exponent always carries its sign and at least two digits.
    /// </remarks>
    private static string Floating(double value, int precision, CultureInfo culture)
    {
        if (value == 0)
            return "0";   // negative zero too: VB6 writes 0 (measured for a Single, a Double and an underflow)
        // VB6 raises 6 or 11 rather than make one of these, but HexIDE can still reach one, and Format already spells
        // them this way (OutOfDecimalRangeNumeric), so every path agrees. Not measured.
        if (double.IsNaN(value))
            return "1.#QNAN";
        if (double.IsInfinity(value))
            return value > 0 ? "1.#INF" : "-1.#INF";

        var (digits, exponent) = SignificantDigits(Math.Abs(value), precision);
        var sign = value < 0 ? "-" : "";
        var point = culture.NumberFormat.NumberDecimalSeparator;

        if (exponent >= precision || (exponent < 0 && -exponent - 1 + digits.Length > precision))
        {
            var mantissa = digits.Length > 1 ? digits[0] + point + digits.Substring(1) : digits;
            return sign + mantissa + "E" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
        }
        if (exponent < 0)
            return sign + "0" + point + new string('0', -exponent - 1) + digits;
        var whole = exponent + 1;
        return digits.Length <= whole
            ? sign + digits + new string('0', whole - digits.Length)
            : sign + digits.Substring(0, whole) + point + digits.Substring(whole);
    }

    /// <summary>
    /// The first <paramref name="precision"/> significant digits of a positive finite <paramref name="value"/>, rounded
    /// half away from zero from its exact binary value, without trailing zeros; and the decimal exponent of the first.
    /// </summary>
    /// <remarks>
    /// Not .NET's <c>"E"</c> format, which rounds an exact tie to even: <c>1234566.5f.ToString("E6")</c> is
    /// <c>1.234566E+006</c> where VB6 writes 1234567. Asking .NET for more digits and rounding those would round
    /// twice. So the value, which is exactly <c>m × 2^e</c>, is divided by the power of ten that leaves
    /// <paramref name="precision"/> digits before the point, in integers, and the remainder decides the last digit.
    /// </remarks>
    private static (string Digits, int Exponent) SignificantDigits(double value, int precision)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var biased = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xF_FFFF_FFFF_FFFFL;
        BigInteger mantissa = biased == 0 ? fraction : fraction | (1L << 52);
        var binary = biased == 0 ? -1074 : biased - 1075;
        var limit = BigInteger.Pow(10, precision);
        var exponent = (int)Math.Floor(Math.Log10(value));   // an estimate: corrected below when it is one out

        while (true)
        {
            var shift = exponent - precision + 1;
            BigInteger numerator = mantissa, denominator = BigInteger.One;
            if (binary > 0) numerator <<= binary; else denominator <<= -binary;
            if (shift > 0) denominator *= BigInteger.Pow(10, shift); else numerator *= BigInteger.Pow(10, -shift);
            var digits = BigInteger.DivRem(numerator, denominator, out var remainder);
            // The estimate is checked before rounding, which would otherwise round at the wrong digit:
            // Log10(999999999999999) is exactly 15 in a Double, one too many.
            if (digits >= limit)
            {
                exponent++;
                continue;
            }
            if (digits * 10 < limit)
            {
                exponent--;
                continue;
            }
            if (remainder * 2 >= denominator)
                digits += 1;   // half away from zero, as measured on an exact tie each way
            return digits == limit
                ? ("1", exponent + 1)   // rounding carried into a new digit: 9999999.5 is 1E+07
                : (digits.ToString(CultureInfo.InvariantCulture).TrimEnd('0'), exponent);
        }
    }

    /// <summary>
    /// A Currency or a Decimal: every digit it holds, without trailing zeros and never in E notation.
    /// <c>CCur(1.5) * 2</c> is <c>3</c>, <c>CDec("1.10")</c> is <c>1.1</c>, and
    /// <c>CDec("0.00000000000000000001")</c> is written out in full (all measured).
    /// </summary>
    private static string Exact(decimal value, CultureInfo culture)
        => value == 0 ? "0" : (value < 0 ? "-" : "") + Math.Abs(value).ToString("0.############################", culture);

    /// <summary>
    /// A Date in the locale's short date and long time, leaving out a midnight time and the zero date: a date only
    /// (<c>02/01/2003</c>), a time only (<c>10:30:00</c>, and <c>00:00:00</c> for the zero date at midnight), or both.
    /// A year below 1000 is not padded (<c>01/01/100</c>, measured). It is shown to the second, as VB6 rounds it
    /// (VbDate.Instant, #783): 12:00:00.6 is <c>12:00:01</c>, and 23:59:59.6 the next day at midnight, so the date alone.
    /// </summary>
    private static string Date(DateTime value, CultureInfo culture)
    {
        var format = VbLocale.ForProgram(culture).DateTimeFormat;
        var time = value.ToString(format.LongTimePattern, format);
        if (value.Date == ZeroDate)
            return time;
        var datePattern = format.ShortDatePattern;
        if (value.Year < 1000)
            datePattern = datePattern.Replace("yyyy", "'" + value.Year.ToString(CultureInfo.InvariantCulture) + "'");
        var date = value.ToString(datePattern, format);
        return value.TimeOfDay == TimeSpan.Zero ? date : date + " " + time;
    }
}
