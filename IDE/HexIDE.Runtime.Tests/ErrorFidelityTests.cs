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

    // vb6.exe SP6 IDE measurements: https://github.com/hexide-io/HexIDE/issues/4730 (oracle PR #4731).
    // Standard help topics: docs/vb6-fidelity-oracle/err-helpfile-err-helpcontext-and-err-lastdllerror.md.
    [Theory]
    [InlineData("&H800A0005", 5, 1000005)]
    [InlineData("&H800A01AD", 429, 1000429)]
    [InlineData("&H800A0001", 1, 1000095)]
    [InlineData("&H800A1000", 4096, 1000095)]
    [InlineData("&H800AFFFF", 65535, 1000095)]
    [InlineData("&H80040154", 429, 1000429)]
    [InlineData("&H800401E3", 429, 1000429)]
    [InlineData("&H800401F3", 429, 1000429)]
    [InlineData("&H80004001", 445, 1000445)]
    [InlineData("&H80004002", 430, 1000430)]
    [InlineData("&H80004004", 287, 1000095)]
    [InlineData("&H8007000E", 7, 1000007)]
    [InlineData("&H80070005", 70, 1000070)]
    [InlineData("&H80070057", 5, 1000005)]
    [InlineData("&H80020003", 438, 1000438)]
    [InlineData("&H80020006", 438, 1000438)]
    [InlineData("&H80020004", 448, 1000448)]
    [InlineData("&H80020005", 13, 1000013)]
    [InlineData("&H80020008", 458, 1000458)]
    [InlineData("&H8002000A", 6, 1000006)]
    [InlineData("&H8002000E", 450, 1000450)]
    [InlineData("&H80030002", 432, 1000432)]
    public async Task RaiseHresult_MapsBeforeDescriptionAndHelp(string input, int number, long help)
    {
        await Run($"On Error Resume Next\nErr.Raise {input}\nDebug.Print Err.Number\nDebug.Print Err.Description\nDebug.Print Err.HelpContext");
        AssertDebugLog([new Vb6Value((long)number), new Vb6Value(VbErrorMessages.For(number)), new Vb6Value(help)]);
    }

    [Theory]
    [InlineData("&H80004005", unchecked((int)0x80004005))]
    [InlineData("&H80004003", unchecked((int)0x80004003))]
    [InlineData("&H8000FFFF", unchecked((int)0x8000FFFF))]
    [InlineData("&H80070002", unchecked((int)0x80070002))]
    [InlineData("&H80020009", unchecked((int)0x80020009))]
    [InlineData("&H80040000", unchecked((int)0x80040000))]
    [InlineData("&H80040005", unchecked((int)0x80040005))]
    [InlineData("&H80040200", unchecked((int)0x80040200))]
    [InlineData("&H800403E8", unchecked((int)0x800403E8))]
    [InlineData("&H8004FFFF", unchecked((int)0x8004FFFF))]
    [InlineData("&H8007FFFF", unchecked((int)0x8007FFFF))]
    [InlineData("&HC0000005", unchecked((int)0xC0000005))]
    [InlineData("&H80000000", unchecked((int)0x80000000))]
    [InlineData("&HFFFFFFFF", unchecked((int)0xFFFFFFFF))]
    [InlineData("&HFFFFFFFB", unchecked((int)0xFFFFFFFB))]
    [InlineData("&HFFFF8000", unchecked((int)0xFFFF8000))]
    public async Task RaiseHresult_KeepsOtherNegatives(string input, int number)
    {
        await Run($"On Error Resume Next\nErr.Raise {input}\nDebug.Print Err.Number\nDebug.Print Err.HelpContext" +
            (OperatingSystem.IsWindows() ? "" : "\nDebug.Print Err.Description"));
        if (OperatingSystem.IsWindows())
            AssertDebugLog([new Vb6Value((long)number), new Vb6Value(1000440L)]);
        else
            AssertDebugLog([new Vb6Value((long)number), new Vb6Value(1000440L), new Vb6Value("Automation error")]);
    }

    [Theory]
    [InlineData("0", 5)]
    [InlineData("65536", 5)]
    [InlineData("&H7FFFFFFF", 5)]
    [InlineData("65535", 65535)]
    public async Task RaiseNumber_MeasuredPositiveCases(string input, long expected)
    {
        await Run($"On Error Resume Next\nErr.Raise {input}\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task RaiseFacilityZero_ClearsAndContinues()
    {
        await Run("Err.Description = \"old\"\nErr.Source = \"old\"\nErr.Raise &H800A0000\nDebug.Print Err.Number\nDebug.Print Err.Description\nDebug.Print Err.HelpContext\nDebug.Print Err.Source");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(""), new Vb6Value(0L), new Vb6Value("")]);
    }

    [Theory]
    [InlineData("&H800A0005", 5)]
    [InlineData("&H80020009", -2147352567)]
    public async Task RaiseHresult_ExplicitDescriptionWins(string input, long number)
    {
        await Run($"On Error Resume Next\nErr.Raise {input}, \"MySrc\", \"My text\"\nDebug.Print Err.Number\nDebug.Print Err.Description\nDebug.Print Err.Source");
        AssertDebugLog([new Vb6Value(number), new Vb6Value("My text"), new Vb6Value("MySrc")]);
    }

    [Fact]
    public async Task NumberAssignment_MapsWithoutSettingDescription()
    {
        await Run("Err.Number = &H800A0005\nDebug.Print Err.Number\nDebug.Print Err.Description");
        AssertDebugLog([new Vb6Value(5L), new Vb6Value("")]);
    }

    [WindowsOnlyFact]
    public async Task KeptHresult_WindowsMessagesMatchMeasurements()
    {
        // #4730: these are English program-facing Err.Description values, not IDE localization keys.
        (uint Code, string Text)[] rows =
        [
            (0x80004005, "Unspecified error "), (0x80004003, "Invalid pointer "),
            (0x8000ffff, "Catastrophic failure "), (0x80070002, "The system cannot find the file specified. "),
            (0x80020009, "Exception occurred. "), (0x80040000, "Invalid OLEVERB structure "),
            (0x80040005, "Need to run the object to perform this operation ")
        ];
        var code = "On Error Resume Next\n";
        List<Vb6Value> expected = [];
        foreach (var row in rows)
        {
            code += $"Err.Clear\nErr.Raise &H{row.Code:X8}\nDebug.Print Err.Description\n";
            expected.Add(new Vb6Value("Automation error\n" + row.Text));
        }
        uint[] noMessage = [0x80040200, 0x800403e8, 0x8004ffff, 0x8007ffff, 0xc0000005,
            0x80000000, 0xffffffff, 0xfffffffb, 0xffff8000];
        foreach (var number in noMessage)
        {
            code += $"Err.Clear\nErr.Raise &H{number:X8}\nDebug.Print Err.Description\n";
            expected.Add(new Vb6Value("Automation error"));
        }
        await Run(code);
        AssertDebugLog(expected);
    }
}
