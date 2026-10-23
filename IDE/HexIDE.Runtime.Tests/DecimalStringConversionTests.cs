using System;
using System.Globalization;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class DecimalStringConversionTests : BaseVBTestFixture
{
    // Native and p-code vb6.exe agree under en-GB (measured, #1531).
    [Theory]
    [InlineData("CDec", "12345678901234567.5", "Decimal 12345678901234567.5")]
    [InlineData("CCur", "123456789012345.6789", "Currency 123456789012345.6789")]
    [InlineData("CCur", "1.23456", "Currency 1.2346")]
    [InlineData("CCur", "1.23455", "Currency 1.2346")]
    [InlineData("CCur", "1.23465", "Currency 1.2346")]
    [InlineData("CCur", "-1.23455", "Currency -1.2346")]
    [InlineData("CDec", "1.123456789012345678901234567890", "Decimal 1.1234567890123456789012345679")]
    [InlineData("CCur", "922337203685477.5807", "Currency 922337203685477.5807")]
    [InlineData("CDec", "79228162514264337593543950335", "Decimal 79228162514264337593543950335")]
    [InlineData("CDec", "0.0000000000000000000000000001", "Decimal 0.0000000000000000000000000001")]
    [InlineData("CCur", "&H10", "Currency 16")]
    [InlineData("CDec", "1E3", "Decimal 1000")]
    public async Task String_conversion_preserves_decimal_precision(string conversion, string input, string expected)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        await Run($"Dim v As Variant\nv = {conversion}(\"{input}\")\nDebug.Print TypeName(v) & \" \" & CStr(v)\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("CCur")]
    [InlineData("CDec")]
    public async Task String_conversion_reads_the_program_locale(string conversion)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("de-DE"));
        await Run($"Debug.Print CStr({conversion}(\"1,5\"))\n");
        AssertDebugLog([new Vb6Value("1,5")]);
    }

    [Theory]
    [InlineData("CCur", "922337203685478")]
    [InlineData("CDec", "79228162514264337593543950336")]
    [InlineData("CCur", "1E30")]
    [InlineData("CDec", "1E30")]
    public async Task Out_of_range_string_raises_error_6(string conversion, string input)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        Func<Task> act = () => Run($"Debug.Print {conversion}(\"{input}\")\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(6);
    }
}
