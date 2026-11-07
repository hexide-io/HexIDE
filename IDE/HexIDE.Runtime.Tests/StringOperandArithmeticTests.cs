using System;
using System.Globalization;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Arithmetic with a String operand (#1047), and the reading of a numeric String it shares with <c>CDbl</c> (#770). Each
/// cell is as vb6.exe gave it under <c>On Error Resume Next</c>: TypeName, value and Err.Number. Native code, p-code and
/// the IDE agree on every one (docs/vb6-fidelity-oracle/a-string-in-arithmetic.md, "A String in arithmetic").
/// </summary>
/// <remarks>
/// Every cell runs in a stated culture, opened with <see cref="VbLocale.Use"/>, because a String is read in the program's
/// locale and the rows differ between en-GB and de-DE. Left to the host it would pass or fail by the runner's culture.
/// </remarks>
public class StringOperandArithmeticTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = new("en-GB");
    private static readonly CultureInfo DeDe = new("de-DE");

    private async Task<string> Cell(string statement, CultureInfo? culture = null)
    {
        using (VbLocale.Use(culture ?? EnGb))
            await Run("Sub P()\r\n    On Error Resume Next\r\n" +
                      "    Dim v As Variant, s As String, i As Integer, vn As Variant, vs As Variant, vt As Variant\r\n" +
                      "    v = Empty\r\n    " + statement + "\r\n" +
                      "    Debug.Print TypeName(v) & \": \" & v & \" Err \" & Err.Number\r\nEnd Sub\r\nP\r\n");
        return (string)debug[^1].Value!;
    }

    // ── every operator reads a numeric String as a Double ────────────────────

    [Theory]
    [InlineData("v = \"6\" * 2", "Double: 12 Err 0")]
    [InlineData("v = \"6\" - \"2\"", "Double: 4 Err 0")]
    [InlineData("v = \"6\" / \"2\"", "Double: 3 Err 0")]
    [InlineData("v = \"6\" + 2", "Double: 8 Err 0")]
    [InlineData("v = 2 + \"6\"", "Double: 8 Err 0")]
    [InlineData("v = \"7\" \\ \"2\"", "Long: 3 Err 0")]
    [InlineData("v = \"7\" Mod \"2\"", "Long: 1 Err 0")]
    [InlineData("v = \"2\" ^ \"3\"", "Double: 8 Err 0")]
    [InlineData("v = -\"6\"", "Double: -6 Err 0")]
    [InlineData("s = \"6\": v = s * 2", "Double: 12 Err 0")]
    [InlineData("s = \"6\": v = s + 2", "Double: 8 Err 0")]
    [InlineData("i = \"6\" * 2: v = i", "Integer: 12 Err 0")]
    [InlineData("v = \"40000\" * 1", "Double: 40000 Err 0")]
    [InlineData("v = \"6\" * CInt(2)", "Double: 12 Err 0")]
    [InlineData("vn = 6: v = vn + \"2\"", "Double: 8 Err 0")]
    [InlineData("vs = \"6\": vn = 2: v = vs + vn", "Double: 8 Err 0")]
    [InlineData("vs = \"6\": vt = \"2\": v = vs * vt", "Double: 12 Err 0")]
    public async Task A_numeric_String_operand_is_read_as_a_number(string statement, string expected)
    {
        (await Cell(statement)).Should().Be(expected);
    }

    // ── a String takes a Double's place in the result table ──────────────────

    [Theory]
    [InlineData("v = \"6\" * CByte(2)", "Double: 12 Err 0")]
    [InlineData("v = \"6\" * CSng(2)", "Double: 12 Err 0")]
    [InlineData("v = \"6\" + CCur(2)", "Currency: 8 Err 0")]
    [InlineData("v = CCur(2) - \"6\"", "Currency: -4 Err 0")]
    [InlineData("v = \"6\" + CDec(2)", "Decimal: 8 Err 0")]
    [InlineData("v = \"6\" * CDec(2)", "Decimal: 12 Err 0")]
    [InlineData("v = \"6\" \\ CInt(2)", "Long: 3 Err 0")]
    [InlineData("v = CInt(2) Mod \"6\"", "Long: 2 Err 0")]
    [InlineData("v = \"6\" + True", "Double: 5 Err 0")]
    [InlineData("v = \"6\" \\ True", "Long: -6 Err 0")]
    [InlineData("v = \"6\" + CDate(2)", "Date: 07/01/1900 Err 0")]
    [InlineData("v = CDate(2) - \"6\"", "Date: 26/12/1899 Err 0")]
    [InlineData("v = CDate(2) * \"6\"", "Double: 12 Err 0")]
    public async Task The_result_type_is_the_one_a_Double_operand_would_give(string statement, string expected)
    {
        // `"6" * CCur(2)` is a Double and `"6" / CDec(2)` a Decimal in VB6, as a Double's would be. HexIDE's own table gets
        // both of those pairs wrong for any Double (#230), so they are not pinned here.
        (await Cell(statement)).Should().Be(expected);
    }

    // ── + concatenates a String with a String or with Empty ──────────────────

    [Theory]
    [InlineData("v = \"6\" + \"2\"", "String: 62 Err 0")]
    [InlineData("vs = \"6\": vt = \"2\": v = vs + vt", "String: 62 Err 0")]
    [InlineData("v = \"6\" + Empty", "String: 6 Err 0")]
    [InlineData("v = Empty + \"6\"", "String: 6 Err 0")]
    [InlineData("v = \"6\" - Empty", "Double: 6 Err 0")]
    [InlineData("v = \"6\" * Empty", "Double: 0 Err 0")]
    [InlineData("v = \"6\" + Null", "Null:  Err 0")]
    public async Task Plus_concatenates_a_String_beside_a_String_or_Empty(string statement, string expected)
    {
        (await Cell(statement)).Should().Be(expected);
    }

    // ── the reading of a String, shared with CDbl ────────────────────────────

    [Theory]
    [InlineData("1e3", "1000")]
    [InlineData("1D3", "1000")]
    [InlineData(" 6 ", "6")]
    [InlineData("&H10", "16")]
    [InlineData("&O10", "8")]
    [InlineData("&HFFFF", "65535")]
    [InlineData("-6", "-6")]
    [InlineData("+6", "6")]
    [InlineData("6-", "-6")]
    [InlineData("(6)", "-6")]
    [InlineData("1,000", "1000")]
    [InlineData("6,5", "65")]
    [InlineData("6.5", "6.5")]
    [InlineData("6.5e1", "65")]
    public async Task A_String_reads_as_CDbl_reads_it_under_en_GB(string text, string expected)
    {
        (await Cell($"v = \"{text}\" * 1")).Should().Be($"Double: {expected} Err 0");
        (await Cell($"v = CDbl(\"{text}\")")).Should().Be($"Double: {expected} Err 0");
    }

    [Theory]
    [InlineData("6,5", "6,5")]
    [InlineData("6.5", "65")]
    [InlineData("1,000", "1")]
    [InlineData("6.5e1", "650")]
    public async Task The_separators_are_the_locales_under_de_DE(string text, string expected)
    {
        (await Cell($"v = \"{text}\" * 1", DeDe)).Should().Be($"Double: {expected} Err 0");
        (await Cell($"v = CDbl(\"{text}\")", DeDe)).Should().Be($"Double: {expected} Err 0");
    }

    [Fact]
    public async Task A_point_is_a_thousands_separator_under_de_DE()
    {
        (await Cell("v = \"1.5\" * 2", DeDe)).Should().Be("Double: 30 Err 0");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("True")]
    [InlineData("0x10")]
    [InlineData("$6")]
    public async Task A_String_that_is_not_a_number_is_Type_mismatch(string text)
    {
        (await Cell($"v = \"{text}\" * 1")).Should().Be("Empty:  Err 13");
        (await Cell($"v = CDbl(\"{text}\")")).Should().Be("Empty:  Err 13");
    }

    [Fact]
    public async Task A_String_too_large_for_a_Double_is_Overflow()
    {
        (await Cell("v = \"1e400\" * 1")).Should().Be("Empty:  Err 6");
    }

    // ── the one shape refused ─────────────────────────────────────────────────

    [Theory]
    [InlineData("v = True \\ \"6\"")]
    [InlineData("v = True Mod \"6\"")]
    public async Task A_Boolean_divided_by_a_String_is_refused_rather_than_answered_wrongly(string statement)
    {
        // Measured: VB6 gives the Boolean True for `True \ "6"` and False for `True Mod "6"`. Read as a Double, the String
        // would give a Long 0, so HexIDE stops there instead.
        var act = () => Cell(statement);
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#1047");
    }
}
