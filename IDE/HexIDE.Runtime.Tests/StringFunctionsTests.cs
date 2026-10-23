using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3 — String intrinsics (1-based indexing; Null propagates). Includes the Mid boundary fix
/// (`Mid("Test",4)` → "t", not "").
/// </summary>
public class StringFunctionsTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Left(\"Hello\", 3)", "Hel")]
    [InlineData("Left(\"Hi\", 5)", "Hi")]
    [InlineData("Right(\"Hello\", 3)", "llo")]
    [InlineData("Mid(\"Hello\", 2, 3)", "ell")]
    [InlineData("Mid(\"Hello\", 3)", "llo")]
    [InlineData("Mid(\"Test\", 4)", "t")]              // boundary fix (was "")
    [InlineData("Mid(\"Test\", 5)", "")]               // start past end -> ""
    [InlineData("Trim(\"  hi  \")", "hi")]
    [InlineData("LTrim(\"  hi  \")", "hi  ")]
    [InlineData("RTrim(\"  hi  \")", "  hi")]
    [InlineData("UCase(\"aBc\")", "ABC")]
    [InlineData("LCase(\"aBc\")", "abc")]
    [InlineData("StrReverse(\"abc\")", "cba")]
    [InlineData("Space(3)", "   ")]
    [InlineData("String(3, \"*\")", "***")]
    [InlineData("String(3, 65)", "AAA")]
    [InlineData("Chr(65)", "A")]
    [InlineData("Replace(\"aXbXc\", \"X\", \"-\")", "a-b-c")]
    [InlineData("Str(5)", " 5")]
    [InlineData("Str(-5)", "-5")]
    public async Task StringFunction_ReturnsExpectedString(string expr, string expected)
    {
        await Run($"Debug.Print {expr}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // Len, InStr and InStrRev return LONG in VB6 however small the answer is — a fixed declared return
    // type, not a magnitude-dependent one. These were asserted as Integer, which pinned HexIDE's bug
    // rather than VB6's behaviour. (#193)
    [Theory]
    [InlineData("Len(\"Hello\")", 5L)]
    [InlineData("InStr(\"Hello World\", \"o\")", 5L)]
    [InlineData("InStr(6, \"Hello World\", \"o\")", 8L)]
    [InlineData("InStr(\"abc\", \"x\")", 0L)]
    [InlineData("InStrRev(\"abcabc\", \"a\")", 4L)]
    public async Task StringFunction_ReturnsExpectedLong(string expr, long expected)
    {
        await Run($"Debug.Print {expr}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // At the ends, measured with vb6.exe, native and p-code alike, and the same with a compare of 0, of 1 and none
    // (#1567): InStr finds an empty string2 at start, however far past the end start is, and an empty string1 holds
    // nothing, an empty string2 included. InStrRev with a start past the end is 0, even where the text is there to find.
    [Theory]
    [InlineData("InStr(1, \"abc\", \"\")", 1L)]
    [InlineData("InStr(3, \"abc\", \"\")", 3L)]
    [InlineData("InStr(4, \"abc\", \"\")", 4L)]
    [InlineData("InStr(5, \"abc\", \"\")", 5L)]
    [InlineData("InStr(100, \"abc\", \"\")", 100L)]
    [InlineData("InStr(1, \"\", \"\")", 0L)]
    [InlineData("InStr(2, \"\", \"\")", 0L)]
    [InlineData("InStr(4, \"abc\", \"a\")", 0L)]
    [InlineData("InStr(5, \"abc\", \"c\")", 0L)]
    [InlineData("InStr(3, \"abc\", \"c\")", 3L)]
    [InlineData("InStr(1, \"abc\", \"\", 0)", 1L)]
    [InlineData("InStr(3, \"abc\", \"\", 0)", 3L)]
    [InlineData("InStr(4, \"abc\", \"\", 0)", 4L)]
    [InlineData("InStr(5, \"abc\", \"\", 0)", 5L)]
    [InlineData("InStr(100, \"abc\", \"\", 0)", 100L)]
    [InlineData("InStr(1, \"\", \"\", 0)", 0L)]
    [InlineData("InStr(2, \"\", \"\", 0)", 0L)]
    [InlineData("InStr(4, \"abc\", \"a\", 0)", 0L)]
    [InlineData("InStr(5, \"abc\", \"c\", 0)", 0L)]
    [InlineData("InStr(3, \"abc\", \"c\", 0)", 3L)]
    [InlineData("InStr(1, \"abc\", \"\", 1)", 1L)]
    [InlineData("InStr(3, \"abc\", \"\", 1)", 3L)]
    [InlineData("InStr(4, \"abc\", \"\", 1)", 4L)]
    [InlineData("InStr(5, \"abc\", \"\", 1)", 5L)]
    [InlineData("InStr(100, \"abc\", \"\", 1)", 100L)]
    [InlineData("InStr(1, \"\", \"\", 1)", 0L)]
    [InlineData("InStr(2, \"\", \"\", 1)", 0L)]
    [InlineData("InStr(4, \"abc\", \"a\", 1)", 0L)]
    [InlineData("InStr(5, \"abc\", \"c\", 1)", 0L)]
    [InlineData("InStr(3, \"abc\", \"c\", 1)", 3L)]
    [InlineData("InStr(\"abc\", \"\")", 1L)]
    [InlineData("InStr(\"\", \"\")", 0L)]
    [InlineData("InStrRev(\"abc\", \"\")", 3L)]
    [InlineData("InStrRev(\"abc\", \"\", 2)", 2L)]
    [InlineData("InStrRev(\"abc\", \"\", 5)", 0L)]
    [InlineData("InStrRev(\"abc\", \"\", 5, 1)", 0L)]
    [InlineData("InStrRev(\"\", \"\")", 0L)]
    [InlineData("InStrRev(\"abc\", \"c\", 4)", 0L)]
    public async Task InStr_and_InStrRev_at_the_ends(string expr, long expected)
    {
        await Run($"Debug.Print {expr}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // Asc, by contrast, really is Integer — measured. It is not one rule applied everywhere, which is why
    // this stayed a separate case rather than being folded into the Long theory above.
    [Theory]
    [InlineData("Asc(\"A\")", 65)]
    public async Task StringFunction_ReturnsExpectedInteger(string expr, int expected)
    {
        await Run($"Debug.Print {expr}\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Val(\"123abc\")", 123.0)]
    [InlineData("Val(\"  1 2 3 \")", 123.0)]          // ignores embedded spaces
    [InlineData("Val(\"3.14xyz\")", 3.14)]
    [InlineData("Val(\"abc\")", 0.0)]
    [InlineData("Val(\"&HFF\")", 255.0)]
    public async Task Val_ParsesLeadingNumeric(string expr, double expected)
    {
        await Run($"Debug.Print {expr}\n");
        AssertDebugLog([new Vb6Value(expected)]);      // Val -> Double
    }

    [Fact]
    public async Task NullArgument_Propagates()
    {
        await Run("Debug.Print UCase(Null)\nDebug.Print Len(Null)\n");
        AssertDebugLog([Vb6Value.Null, Vb6Value.Null]);
    }

    [Fact]
    public async Task UserFunction_ShadowsBuiltin()
    {
        // The builtin table is consulted strictly last, so a user Function Left() wins.
        await Run(
            "Function Left(ByVal n As Integer) As Integer\n" +
            "Left = n * 2\n" +
            "End Function\n" +
            "Debug.Print Left(21)\n");
        AssertDebugLog([new Vb6Value(42)]);
    }
}
