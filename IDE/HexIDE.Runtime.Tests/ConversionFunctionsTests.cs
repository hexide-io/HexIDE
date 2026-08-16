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
        AssertDebugLog([new Vb6Value(new DateTime(2000, 1, 1)), new Vb6Value(new DateTime(2000, 1, 1))]);
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
}
