using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured with vb6.exe, native and p-code identical (#1512).
public class IntrinsicTooFewArgumentsTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Abs()", "Syntax error")]
    [InlineData("Int()", "Syntax error")]
    [InlineData("Fix()", "Syntax error")]
    [InlineData("Sgn()", "Syntax error")]
    [InlineData("CBool()", "Syntax error")]
    [InlineData("CByte()", "Syntax error")]
    [InlineData("CCur()", "Syntax error")]
    [InlineData("CDate()", "Syntax error")]
    [InlineData("CDbl()", "Syntax error")]
    [InlineData("CInt()", "Syntax error")]
    [InlineData("CLng()", "Syntax error")]
    [InlineData("CSng()", "Syntax error")]
    [InlineData("CStr()", "Syntax error")]
    [InlineData("CVar()", "Syntax error")]
    [InlineData("CVErr()", "Syntax error")]
    [InlineData("Len()", "Syntax error")]
    [InlineData("LenB()", "Syntax error")]
    [InlineData("LBound()", "Syntax error")]
    [InlineData("UBound()", "Syntax error")]
    [InlineData("InStr(\"a\")", "Syntax error")]
    [InlineData("StrComp(\"a\")", "Syntax error")]
    [InlineData("String()", "Syntax error")]
    [InlineData("String(3)", "Syntax error")]
    [InlineData("CDec()", "Argument not optional")]
    [InlineData("CVDate()", "Argument not optional")]
    [InlineData("Chr()", "Argument not optional")]
    [InlineData("Chr$()", "Argument not optional")]
    [InlineData("ChrW()", "Argument not optional")]
    [InlineData("Asc()", "Argument not optional")]
    [InlineData("AscW()", "Argument not optional")]
    [InlineData("Hex()", "Argument not optional")]
    [InlineData("Oct()", "Argument not optional")]
    [InlineData("Str()", "Argument not optional")]
    [InlineData("Val()", "Argument not optional")]
    [InlineData("LCase()", "Argument not optional")]
    [InlineData("UCase()", "Argument not optional")]
    [InlineData("Trim()", "Argument not optional")]
    [InlineData("Space()", "Argument not optional")]
    [InlineData("Round()", "Argument not optional")]
    [InlineData("IsNull()", "Argument not optional")]
    [InlineData("TypeName()", "Argument not optional")]
    [InlineData("VarType()", "Argument not optional")]
    [InlineData("Format()", "Argument not optional")]
    [InlineData("Split()", "Argument not optional")]
    [InlineData("Join()", "Argument not optional")]
    [InlineData("Mid(\"abc\")", "Argument not optional")]
    [InlineData("Mid$(\"abc\")", "Argument not optional")]
    [InlineData("Left(\"abc\")", "Argument not optional")]
    [InlineData("Right(\"abc\")", "Argument not optional")]
    [InlineData("Mid(\"abc\", , 2)", "Argument not optional")]
    [InlineData("Replace(\"a\",\"b\")", "Argument not optional")]
    [InlineData("IIf(True,1)", "Argument not optional")]
    [InlineData("DateAdd(\"d\",1)", "Argument not optional")]
    public async Task A_missing_required_argument_raises_VB6s_compile_error(string call, string message)
    {
        var run = () => Run("Debug.Print " + call + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("Compile error:\n\n" + message);
    }

    [Fact]
    public async Task Array_with_no_arguments_is_an_empty_Variant_array()
    {
        await Run("""
            Dim a As Variant
            a = Array()
            Debug.Print IsArray(a), TypeName(a), UBound(a)

            """);

        AssertDebugLog([new Vb6Value(true), new Vb6Value("Variant()"), new Vb6Value(-1L)]);
    }
}
