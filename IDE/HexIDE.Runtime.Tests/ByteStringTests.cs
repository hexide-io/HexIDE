using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The byte-string functions (#1510): LenB, AscB, InStrB, MidB, LeftB and RightB. Every expected value was measured on vb6.exe
/// SP6, native and p-code alike: docs/vb6-fidelity-oracle/the-byte-string-functions.md. A result of an odd number of
/// bytes, and so ChrB, is refused: HexIDE's strings are whole characters and cannot hold one.
/// </summary>
public class ByteStringTests : BaseVBTestFixture
{
    private async Task<string?> Printed(string expression)
    {
        await Run($"Debug.Print CStr({expression})\n");
        return debug.Single().Value as string;
    }

    [Theory]
    [InlineData("LenB(\"abc\")", "6")]
    [InlineData("TypeName(LenB(\"abc\"))", "Long")]
    [InlineData("LenB(Empty)", "0")]
    [InlineData("IsNull(LenB(Null))", "True")]
    [InlineData("AscB(\"A\")", "65")]
    [InlineData("TypeName(AscB(\"A\"))", "Byte")]
    [InlineData("AscB(ChrW(&H416))", "22")]
    [InlineData("MidB(\"abc\", 3)", "bc")]
    [InlineData("MidB$(\"abc\", 3)", "bc")]
    [InlineData("MidB(\"abc\", 3, 100)", "bc")]
    [InlineData("MidB(\"abc\", 7)", "")]
    [InlineData("MidB(\"abc\", 1, 0)", "")]
    [InlineData("AscW(MidB(\"abc\", 2, 2))", "25088")]
    [InlineData("LenB(MidB(\"abc\", 2, 4))", "4")]
    [InlineData("TypeName(MidB(\"abc\", 3))", "String")]
    [InlineData("IsNull(MidB(Null, 1))", "True")]
    [InlineData("LeftB(\"abc\", 4)", "ab")]
    [InlineData("LeftB$(\"abc\", 2)", "a")]
    [InlineData("LeftB(\"abc\", 10)", "abc")]
    [InlineData("LeftB(\"abc\", 0)", "")]
    [InlineData("IsNull(LeftB(Null, 1))", "True")]
    [InlineData("RightB(\"abc\", 4)", "bc")]
    [InlineData("RightB(\"abc\", 10)", "abc")]
    [InlineData("RightB(\"abc\", 0)", "")]
    [InlineData("IsNull(RightB(Null, 1))", "True")]
    [InlineData("InStrB(\"abc\", \"b\")", "3")]
    [InlineData("InStrB(1, \"abc\", \"b\")", "3")]
    [InlineData("InStrB(3, \"abc\", \"b\")", "3")]
    [InlineData("InStrB(4, \"abc\", \"b\")", "0")]
    [InlineData("InStrB(1, \"ABC\", \"b\", vbTextCompare)", "0")]
    [InlineData("InStrB(1, \"ABC\", \"b\", vbBinaryCompare)", "0")]
    [InlineData("InStrB(\"abc\", \"\")", "1")]
    [InlineData("InStrB(6, \"abc\", \"\")", "6")]
    [InlineData("InStrB(7, \"abc\", \"\")", "0")]
    [InlineData("InStrB(\"\", \"\")", "0")]
    [InlineData("InStrB(\"abc\", ChrW(&H6200))", "2")]   // a match that starts inside a character
    [InlineData("TypeName(InStrB(\"abc\", \"b\"))", "Long")]
    [InlineData("IsNull(InStrB(Null, \"a\"))", "True")]
    public async Task A_byte_string_function_gives_what_vb6_gave(string expression, string expected)
    {
        (await Printed(expression)).Should().Be(expected);
    }

    [Theory]
    [InlineData("AscB(\"\")", 5)]
    [InlineData("MidB(\"abc\", 0, 1)", 5)]
    [InlineData("MidB(\"abc\", 1, -1)", 5)]
    [InlineData("LeftB(\"abc\", -1)", 5)]
    [InlineData("RightB(\"abc\", -1)", 5)]
    [InlineData("InStrB(0, \"abc\", \"b\")", 5)]
    [InlineData("AscB(Null)", 94)]
    [InlineData("MidB$(Null, 1)", 94)]
    public async Task A_byte_string_function_raises_what_vb6_raised(string expression, int error)
    {
        await Run($"On Error Resume Next\nDim r\nr = {expression}\nDebug.Print CStr(Err.Number)\n");

        (debug.Single().Value as string).Should().Be(error.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task LenB_of_a_declared_scalar_is_its_size_and_of_a_Variant_twice_its_text()
    {
        await Run("""
            Dim vi As Integer, vl As Long, vd As Double, vc As Currency, vdt As Date, vb As Byte, vbo As Boolean, vs As String
            Dim vsng As Single, vv As Variant
            vs = "abcd": vv = 12345
            Debug.Print CStr(LenB(vi)) & " " & CStr(LenB(vl)) & " " & CStr(LenB(vd)) & " " & CStr(LenB(vc)) & " " & CStr(LenB(vdt)) & " " & CStr(LenB(vb)) & " " & CStr(LenB(vbo)) & " " & CStr(LenB(vs))
            Debug.Print CStr(LenB(vsng)) & " " & CStr(LenB(vv)) & " " & CStr(Len(vv))
            """);

        debug.Select(v => v.Value as string).Should().Equal("2 4 8 8 8 1 2 8", "4 10 5");
    }

    [Theory]
    [InlineData("LenB(5)")]
    [InlineData("LenB(5&)")]
    [InlineData("LenB(1.5)")]
    [InlineData("LenB(True)")]
    [InlineData("LenB(2 + 3)")]
    public async Task LenB_of_a_number_that_is_not_a_variable_is_a_compile_error(string expression)
    {
        var run = () => Run($"Dim r\nr = {expression}\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Variable required*");
    }

    [Theory]
    [InlineData("ChrB(65)")]
    [InlineData("MidB(\"abc\", 2, 3)")]
    [InlineData("LeftB(\"abc\", 3)")]
    [InlineData("RightB(\"abc\", 1)")]
    public async Task A_string_of_an_odd_number_of_bytes_is_refused_rather_than_rounded(string expression)
    {
        var run = () => Run($"On Error Resume Next\nDim r\nr = {expression}\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*odd number of bytes*");
    }
}
