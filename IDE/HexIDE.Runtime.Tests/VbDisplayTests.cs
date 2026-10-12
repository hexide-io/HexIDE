using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The text VB6 makes of a Single, a Double, a Currency, a Decimal and a Date (#772, #771). Every expectation is a
/// row measured on vb6.exe under an en-GB locale (docs/vb6-fidelity-oracle.md, "Turning a number or a date into
/// text"), so each is asserted in en-GB explicitly: CI runs in the invariant culture and a developer may run in
/// any, and the date patterns differ between them.
/// </summary>
public class VbDisplayTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    private static string Text(Vb6Value value) => VbDisplay.ToText(value, EnGb);

    // CSng(x) in the probes rounds a Double literal to a Single, so the C# cases do the same rather than parsing
    // straight to float, which could differ by a unit in the last place.
    [Theory]
    [InlineData(1234567d, "1234567")]
    [InlineData(12345678d, "1.234568E+07")]
    [InlineData(1000000d, "1000000")]
    [InlineData(10000000d, "1E+07")]
    [InlineData(99999990d, "9.999999E+07")]
    [InlineData(15000000d, "1.5E+07")]
    [InlineData(1E+15, "1E+15")]
    [InlineData(1E+38, "1E+38")]
    [InlineData(0.1, "0.1")]
    [InlineData(0.001, "0.001")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "0.00001")]
    [InlineData(0.000012345, "1.2345E-05")]
    [InlineData(1.5E-10, "1.5E-10")]
    [InlineData(1E-38, "9.999999E-39")]
    [InlineData(123456.78, "123456.8")]
    [InlineData(9999999.5, "1E+07")]
    [InlineData(0.5, "0.5")]
    [InlineData(100d, "100")]
    // Where E notation starts below 1: when the zeros after the point and the significant digits exceed seven.
    [InlineData(0.0000001, "0.0000001")]
    [InlineData(0.00000001, "1E-08")]
    [InlineData(0.0000015, "0.0000015")]
    [InlineData(0.00000015, "1.5E-07")]
    [InlineData(0.0012345, "0.0012345")]
    [InlineData(0.00012345, "1.2345E-04")]
    [InlineData(0.1234567, "0.1234567")]
    [InlineData(0.01234567, "1.234567E-02")]
    [InlineData(999999.95, "999999.9")]
    // Exact ties round away from zero: half-to-even would give 1234566.
    [InlineData(1234566.5, "1234567")]
    [InlineData(1234567.5, "1234568")]
    [InlineData(1.0000005, "1")]
    public void A_Single_has_seven_significant_digits(double probe, string expected)
        => Text(new Vb6Value((float)probe)).Should().Be(expected);

    [Fact]
    public void A_Single_computed_from_a_division_has_seven()
    {
        Text(new Vb6Value(-1f / 3)).Should().Be("-0.3333333");
        Text(new Vb6Value(2f / 3)).Should().Be("0.6666667");
        Text(new Vb6Value(1f / 3)).Should().Be("0.3333333");
    }

    [Theory]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(1d / 3, "0.333333333333333")]
    [InlineData(2d / 3, "0.666666666666667")]
    [InlineData(123456789012345d, "123456789012345")]
    [InlineData(1234567890123456d, "1.23456789012346E+15")]
    [InlineData(1E+14, "100000000000000")]
    [InlineData(1E+15, "1E+15")]
    [InlineData(1E+16, "1E+16")]
    [InlineData(999999999999999d, "999999999999999")]
    // 9999999999999999# was measured too, and is the same row: the literal is already 1E+16 as a Double.
    [InlineData(9007199254740992d, "9.00719925474099E+15")]
    [InlineData(1E+21, "1E+21")]
    [InlineData(1E+100, "1E+100")]
    [InlineData(1.79769313486231E+308, "1.79769313486231E+308")]
    [InlineData(0.0001, "0.0001")]
    [InlineData(0.00001, "0.00001")]
    [InlineData(0.000012345, "0.000012345")]
    [InlineData(0.00001 / 3, "3.33333333333333E-06")]
    [InlineData(1E-100, "1E-100")]
    [InlineData(4.94065645841247E-324, "4.94065645841247E-324")]
    [InlineData(-0.1 - 0.2, "-0.3")]
    [InlineData(100d, "100")]
    [InlineData(1.5, "1.5")]
    [InlineData(1d / 7, "0.142857142857143")]
    [InlineData(22d / 7 * 1000000, "3142857.14285714")]
    // Where E notation starts below 1, against fifteen digits.
    [InlineData(1E-15, "0.000000000000001")]
    [InlineData(1E-16, "1E-16")]
    [InlineData(1.5E-14, "0.000000000000015")]
    [InlineData(1.5E-15, "1.5E-15")]
    [InlineData(0.0123456789012345, "1.23456789012345E-02")]
    [InlineData(0.123456789012345, "0.123456789012345")]
    // Exact ties round away from zero: half-to-even would give 1.23456789012344E+15.
    [InlineData(1234567890123445d, "1.23456789012345E+15")]
    [InlineData(1234567890123455d, "1.23456789012346E+15")]
    [InlineData(1.5E+15, "1.5E+15")]
    [InlineData(-1E+15, "-1E+15")]
    [InlineData(-0.00000015, "-0.00000015")]
    public void A_Double_has_fifteen_significant_digits(double probe, string expected)
        => Text(new Vb6Value(probe)).Should().Be(expected);

    [Fact]
    public void Negative_zero_is_written_as_zero()
    {
        Text(new Vb6Value(-0.0)).Should().Be("0");
        Text(new Vb6Value(-0f)).Should().Be("0");
        Text(new Vb6Value(-1E-200 * 1E-200)).Should().Be("0", "an underflow to negative zero, as measured");
        Text(Vb6Value.NewCurrency(-0.0m)).Should().Be("0");
    }

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("100", "100")]
    [InlineData("1234567.8912", "1234567.8912")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("-2.5", "-2.5")]
    [InlineData("922337203685477.5807", "922337203685477.5807")]
    [InlineData("3.0", "3")]       // CCur(1.5) * 2
    [InlineData("2.50", "2.5")]    // CCur(1.25) + CCur(1.25)
    [InlineData("1.50", "1.5")]    // CCur("1.50")
    public void A_Currency_drops_its_trailing_zeros(string stored, string expected)
        => Text(Vb6Value.NewCurrency(decimal.Parse(stored, CultureInfo.InvariantCulture))).Should().Be(expected);

    [Theory]
    [InlineData("1.10", "1.1")]
    [InlineData("100.00", "100")]
    [InlineData("0.00000000000000000001", "0.00000000000000000001")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    public void A_Decimal_drops_its_trailing_zeros_and_is_never_in_E_notation(string stored, string expected)
        => Text(Vb6Value.NewDecimal(decimal.Parse(stored, CultureInfo.InvariantCulture))).Should().Be(expected);

    [Fact]
    public void A_Decimal_division_keeps_every_digit()
        => Text(Vb6Value.NewDecimal(1m / 3m)).Should().Be("0.3333333333333333333333333333");

    public static TheoryData<DateTime, string> Dates => new()
    {
        { new DateTime(2003, 1, 2), "02/01/2003" },
        { new DateTime(2003, 1, 2, 10, 30, 0), "02/01/2003 10:30:00" },
        { new DateTime(2003, 1, 2, 22, 30, 5), "02/01/2003 22:30:05" },
        { new DateTime(2003, 1, 2, 0, 0, 1), "02/01/2003 00:00:01" },
        { new DateTime(2003, 1, 2, 12, 0, 0), "02/01/2003 12:00:00" },
        { new DateTime(1899, 12, 30, 10, 30, 0), "10:30:00" },
        { new DateTime(1899, 12, 30), "00:00:00" },
        { new DateTime(1899, 12, 30, 12, 0, 0), "12:00:00" },
        { new DateTime(1899, 12, 30, 0, 0, 1), "00:00:01" },
        { new DateTime(1899, 12, 29), "29/12/1899" },
        { DateTime.FromOADate(-1.5), "29/12/1899 12:00:00" },
        { new DateTime(100, 1, 1), "01/01/100" },
        { new DateTime(999, 12, 31), "31/12/999" },
        { new DateTime(9999, 12, 31, 23, 59, 59), "31/12/9999 23:59:59" },
    };

    [Theory]
    [MemberData(nameof(Dates))]
    public void A_Date_leaves_out_a_midnight_time_and_the_zero_date(DateTime probe, string expected)
        => Text(new Vb6Value(probe)).Should().Be(expected);

    [Fact]
    public void An_omitted_argument_has_no_text_and_an_empty_string_has_empty_text()
    {
        VbDisplay.ToTextOrNull(Vb6Value.Missing).Should().BeNull();
        VbDisplay.ToTextOrNull(new Vb6Value("")).Should().Be("");
        VbDisplay.ToTextOrNull(new Vb6Value(0.5)).Should().Be("0.5");
    }

    // ── Through the interpreter: each path that turns a value into text ──────────────────────────────────

    [Theory]
    [InlineData("CStr(CSng(1) / 3)", "0.3333333")]
    [InlineData("CStr(-0#)", "0")]
    [InlineData("CStr(CCur(1.5) * 2)", "3")]
    [InlineData("CStr(#1/2/2003#)", "02/01/2003")]
    [InlineData("CStr(#10:30:00 AM#)", "10:30:00")]
    [InlineData("CStr(CDate(\"10:30:00\"))", "10:30:00")]
    [InlineData("\"[\" & (0.1 + 0.2) & \"]\"", "[0.3]")]
    [InlineData("\"[\" & CSng(1) / 3 & \"]\"", "[0.3333333]")]
    [InlineData("\"[\" & #1/2/2003# & \"]\"", "[02/01/2003]")]
    [InlineData("\"[\" & #10:30:00 AM# & \"]\"", "[10:30:00]")]
    [InlineData("\"[\" & 1E+15 & \"]\"", "[1E+15]")]
    [InlineData("\"[\" & CCur(1.5) & \"]\"", "[1.5]")]
    [InlineData("Format(CSng(1) / 3)", "0.3333333")]
    [InlineData("Format(1E+15)", "1E+15")]
    [InlineData("Format(CSng(0.000012345))", "1.2345E-05")]
    [InlineData("Format(0.1 + 0.2)", "0.3")]
    [InlineData("Format(#1/2/2003#)", "02/01/2003")]
    [InlineData("Format(#10:30:00 AM#)", "10:30:00")]
    [InlineData("Format(#1/2/2003 10:30:00 AM#)", "02/01/2003 10:30:00")]
    [InlineData("Str(0.5)", " .5")]
    [InlineData("Str(-0.5)", "-.5")]
    [InlineData("Str(1E+15)", " 1E+15")]
    [InlineData("Str(0.00000015)", " .00000015")]
    [InlineData("Str(CSng(10000000))", " 1E+07")]
    [InlineData("Str(CCur(1.5))", " 1.5")]
    [InlineData("Str(0)", " 0")]
    [InlineData("Len(CSng(1) / 3) & \"\"", "9")]
    public async Task Each_path_to_text_writes_what_VB6_writes(string expression, string expected)
        => (await Printed($"Debug.Print {expression}\n")).Should().Be(expected);

    [Fact]
    public async Task Storing_into_a_String_variable_writes_the_same_text()
        => (await Printed("Dim s As String\ns = CSng(1) / 3\nDebug.Print s\n")).Should().Be("0.3333333");

    [Theory]
    [InlineData("CDbl(#10:30:00 AM#)")]
    [InlineData("CDbl(CDate(\"10:30:00\"))")]
    public async Task A_time_with_no_date_is_on_the_zero_date_not_today(string expression)
    {
        await Run($"Debug.Print {expression}\n");

        AssertDebugLog([0.4375]);
    }

    private async Task<string> Printed(string code)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = EnGb;
        try
        {
            await Run(code);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
        return debug.Should().ContainSingle().Which.Value.Should().BeOfType<string>().Subject;
    }
}
