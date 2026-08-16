using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for the adversarial bug-hunt MED error-fidelity cluster: the interpreter threw raw, UNTRAPPABLE .NET
/// exceptions (OverflowException, ArgumentOutOfRangeException, ArgumentException) where VB6 raises a TRAPPABLE error
/// that `On Error` catches. Every error number is oracle-pinned against vb6.exe.
/// </summary>
public class ErrorFidelityTests : BaseVBTestFixture
{
    // Negative / inverted array bounds -> Err 9 (Subscript out of range), not an uncatchable OverflowException.
    [Theory]
    [InlineData("ReDim a(-2)")]
    [InlineData("ReDim a(2 To 0)")]
    [InlineData("ReDim a(5 To 1)")]
    public async Task InvertedArrayBounds_RaiseSubscriptOutOfRange(string redim)
    {
        await Run("On Error Resume Next\nDim a()\n" + redim + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(9L)]);
    }

    // UBound/LBound with a dimension < 1 or > rank -> Err 9, not ArgumentOutOfRangeException.
    [Theory]
    [InlineData("UBound(a, 2)")]
    [InlineData("LBound(a, 0)")]
    public async Task BadBoundDimension_RaisesSubscriptOutOfRange(string call)
    {
        await Run("On Error Resume Next\nDim a(3)\nDim x\nx = " + call + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(9L)]);
    }

    // CDate of a serial outside the Date range (about -657434 .. 2958465) -> Err 6 (Overflow), not an uncatchable
    // ArgumentException.
    [Theory]
    [InlineData("1E+30")]
    [InlineData("2958466")]
    [InlineData("-657435")]
    public async Task CDateOutOfRange_RaisesOverflow(string serial)
    {
        await Run("On Error Resume Next\nDim d\nd = CDate(" + serial + ")\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(6L)]);
    }

    // Access (read or write) of an undimensioned dynamic array -> Err 9 (Subscript out of range), not an uncatchable
    // VBCompileErrorException ("Dimension doesn't match").
    [Theory]
    [InlineData("a(0) = 1")]
    [InlineData("x = a(0)")]
    public async Task UndimensionedArrayAccess_RaisesSubscriptOutOfRange(string stmt)
    {
        await Run("On Error Resume Next\nDim a() As Integer\nDim x\n" + stmt + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(9L)]);
    }

    // DateAdd past the Date range -> Err 5 (Invalid procedure call), oracle-verified as DISTINCT from TimeSerial's
    // Err 6 (both were uncatchable ArgumentOutOfRangeException before).
    [Fact]
    public async Task DateAddOverflow_RaisesInvalidProcedureCall()
    {
        await Run("On Error Resume Next\nDim d\nd = DateAdd(\"yyyy\", 100000, Now)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L)]);
    }

    // TimeSerial whose hour count pushes past the Date range -> Err 6 (Overflow), not an uncatchable
    // ArgumentOutOfRangeException. Oracle-verified as DISTINCT from DateAdd's Err 5 (vb6.exe: TimeSerial(9999999,..)
    // = ERR6). NB VB6 coerces the args to Integer, so it actually errors at ~32768 hours; the interpreter coerces
    // wider and only overflows the DateTime range at ~1e8 hours (a documented approximation gap — see
    // docs/interpreter-gaps.md). Either way the crash is now a trappable Err 6.
    [Fact]
    public async Task TimeSerialOverflow_RaisesOverflow()
    {
        await Run("On Error Resume Next\nDim d\nd = TimeSerial(100000000, 0, 0)\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(6L)]);
    }
}
