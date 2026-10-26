using System;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3 — Conversion intrinsics. Every expectation verified against vb6.exe: Hex/Oct width follows the
/// operand type, CStr has no leading space, CByte/CInt overflow -> Err 6, CCur/CDec/CDate typed correctly.
/// </summary>
public class ConversionFunctionsTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Hex(255)", "FF")]
    [InlineData("Hex(-1)", "FFFF")]              // Integer -1 -> 16-bit
    [InlineData("Hex(CLng(-1))", "FFFFFFFF")]    // Long -1 -> 32-bit
    [InlineData("Hex(256)", "100")]
    [InlineData("Oct(8)", "10")]
    [InlineData("Oct(-1)", "177777")]            // Integer -1 -> 16-bit octal
    [InlineData("CStr(5)", "5")]                 // no leading space (unlike Str)
    [InlineData("CStr(3.14)", "3.14")]
    [InlineData("CStr(True)", "True")]
    [InlineData("CStr(-5)", "-5")]
    public async Task ConversionReturningString(string expr, string expected)
    {
        await Run($"Debug.Print {expr}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("CBool(5)", true)]
    [InlineData("CBool(0)", false)]
    [InlineData("CBool(\"True\")", true)]
    [InlineData("CBool(Empty)", false)]      // measured: False, no error, as CLng(Empty) is 0
    public async Task CBool_ReturnsBoolean(string expr, bool expected)
    {
        await Run($"Debug.Print {expr}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task NumericConversions_ProduceTheDeclaredType()
    {
        await Run(
            "Debug.Print CByte(2.5)\n" +   // banker's -> 2, Byte
            "Debug.Print CByte(3.5)\n" +   // banker's -> 4, Byte
            "Debug.Print CInt(\"42\")\n" + // parses the string -> Integer
            "Debug.Print CLng(5)\n" +      // Long
            "Debug.Print CDbl(\"3.14\")\n" +
            "Debug.Print CSng(2.5)\n" +
            "Debug.Print CCur(5)\n" +      // Currency
            "Debug.Print CDec(5)\n");      // Decimal
        AssertDebugLog([
            new Vb6Value((byte)2),
            new Vb6Value((byte)4),
            new Vb6Value(42),
            new Vb6Value(5L),
            new Vb6Value(3.14),
            new Vb6Value(2.5f),
            Vb6Value.NewCurrency(5m),
            Vb6Value.NewDecimal(5m),
        ]);
    }

    [Fact]
    public async Task CDate_ParsesStringAndSerial()
    {
        await Run("Debug.Print CDate(\"1/1/2000\")\nDebug.Print CDate(36526)\n");
        AssertDebugLog([new Vb6Value(VbDate.FromDateTime(new DateTime(2000, 1, 1))), new Vb6Value(VbDate.FromDateTime(new DateTime(2000, 1, 1)))]);
    }

    [Theory]
    [InlineData("CByte(-1)")]     // < 0
    [InlineData("CByte(300)")]    // > 255
    [InlineData("CInt(40000)")]   // > Int16
    public async Task Conversion_Overflow_RaisesErr6(string expr)
    {
        Func<Task> act = () => Run($"Debug.Print {expr}\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    [Theory]
    [InlineData("CDbl(\"NaN\")")]
    [InlineData("CDbl(\"Infinity\")")]
    [InlineData("CDbl(\"-Infinity\")")]
    [InlineData("CLng(\"NaN\")")]
    [InlineData("CBool(\"\")")]
    [InlineData("CBool(\"NaN\")")]
    [InlineData("CBool(\"Infinity\")")]
    [InlineData("CBool(\"-Infinity\")")]
    [InlineData("CBool(\"1.#INF\")")]
    public async Task A_string_that_is_not_a_VB6_number_is_a_type_mismatch(string expr)
    {
        // Measured on vb6.exe. .NET reads "NaN" and "Infinity" as numbers; VB6 has neither.
        Func<Task> act = () => Run($"Debug.Print {expr}\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(13);
    }

    [Theory]
    [InlineData("CDbl(\"1E+400\")")]
    [InlineData("CBool(\"1E+400\")")]
    public async Task Digits_too_large_for_a_Double_are_an_overflow_not_a_type_mismatch(string expr)
    {
        // .NET parses "1E+400" to Infinity, as it parses "Infinity". Measured: VB6 raises 6 for both.
        Func<Task> act = () => Run($"Debug.Print {expr}\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    [Theory]
    [InlineData("CDbl", "1E+400")]
    [InlineData("Val", "1E+400")]
    [InlineData("CSng", "1E+39")]
    public async Task Floating_point_string_overflow_raises_error_6(string conversion, string input)
    {
        Func<Task> act = () => Run($"Debug.Print {conversion}(\"{input}\")\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }

    [Fact]
    public async Task CBool_reads_a_hex_string_as_the_number_it_is()
    {
        // Measured: CBool("&H10") is True. CBool had a parse of its own that knew no &H.
        await Run("Debug.Print CBool(\"&H10\")\n");
        AssertDebugLog([new Vb6Value(true)]);
    }
}
