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
