using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured on vb6.exe SP6, native and p-code: #2623, with oracle measurements in #2624 and #2628.
public class MidStatementSpellingTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Mid(s, 2, 2) = \"XY\"", "aXYdef", 0)]
    [InlineData("Mid$(s, 2, 2) = \"XY\"", "aXYdef", 0)]
    [InlineData("Mid$(s, 2) = \"XYZ\"", "aXYZef", 0)]
    [InlineData("Mid$(s, 5) = \"WXYZ\"", "abcdWX", 0)]
    [InlineData("Mid$(s, 2, 5) = \"X\"", "aXcdef", 0)]
    [InlineData("Mid$(s, 7) = \"X\"", "abcdef", 5)]
    [InlineData("Mid$(s, 0) = \"X\"", "abcdef", 5)]
    [InlineData("Mid$(s, 2, 1) = Null", "abcdef", 94)]
    [InlineData("Mid(s, 2, 1) = Null", "abcdef", 94)]
    public async Task Character_statement_matches_measured_result(string statement, string expected, int error)
    {
        await Run($"On Error Resume Next\nDim s As String\ns = \"abcdef\"\n{statement}\nDebug.Print Err.Number\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value((long)error), new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("MidB(s, 3, 2) = \"X\"", "97 88 99 100 101 102")]
    [InlineData("MidB$(s, 3) = \"XY\"", "97 88 89 100 101 102")]
    [InlineData("MidB(s, 2, 2) = \"X\"", "22625 0 99 100 101 102")]
    [InlineData("MidB(s, 3, 1) = \"XY\"", "97 88 99 100 101 102")]
    public async Task Byte_statement_preserves_the_measured_character_units(string statement, string expected)
    {
        await Run($"""
            On Error Resume Next
            Dim s As String
            s = "abcdef"
            {statement}
            Debug.Print Err.Number
            Debug.Print CStr(AscW(Mid(s, 1, 1))) & " " & CStr(AscW(Mid(s, 2, 1))) & " " & CStr(AscW(Mid(s, 3, 1))) & " " & CStr(AscW(Mid(s, 4, 1))) & " " & CStr(AscW(Mid(s, 5, 1))) & " " & CStr(AscW(Mid(s, 6, 1)))
            """);
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("\"abcdef\"", "Mid$(v, 2, 1) = \"Z\"", "aZcdef")]
    [InlineData("12345", "Mid$(v, 2, 1) = \"9\"", "19345")]
    public async Task Variant_target_becomes_the_measured_string(string initial, string statement, string expected)
    {
        await Run($"On Error Resume Next\nDim v As Variant\nv = {initial}\n{statement}\nDebug.Print Err.Number\nDebug.Print v\nDebug.Print TypeName(v)\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(expected), new Vb6Value("String")]);
    }

    [Theory]
    [InlineData("Mid$")]
    [InlineData("Mid")]
    public async Task Null_target_raises_94_and_stays_Null(string spelling)
    {
        await Run($"On Error Resume Next\nDim v As Variant\nv = Null\n{spelling}(v, 1, 1) = \"9\"\nDebug.Print Err.Number\nDebug.Print IsNull(v)\n");
        AssertDebugLog([new Vb6Value(94L), new Vb6Value(true)]);
    }
}
