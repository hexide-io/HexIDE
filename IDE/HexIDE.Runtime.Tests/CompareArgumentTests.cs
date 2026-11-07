using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #881: vb6.exe SP6, native and p-code agreeing (issue measurements).
public class CompareArgumentTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("InStr(1, \"abc\", \"B\", 2)", "0", 5)]
    [InlineData("InStr(5, \"abc\", \"x\", 2)", "0", 5)]
    [InlineData("InStr(1, \"a\", \"long\", 2)", "0", 5)]
    [InlineData("InStr(1, Null, \"a\", 2)", "Null", 0)]
    [InlineData("InStr(1, \"\", \"a\", 2)", "0", 0)]
    [InlineData("InStr(5, \"abc\", \"\", 2)", "5", 0)]
    [InlineData("InStrRev(\"aXb\", \"x\", -1, 2)", "2", 0)]
    [InlineData("InStrRev(\"\", \"x\", -1, 2)", "0", 0)]
    [InlineData("UBound(Split(\"aXb\", \"x\", -1, 2))", "1", 0)]
    [InlineData("UBound(Split(\"\", \",\", -1, 2))", "-1", 0)]
    [InlineData("UBound(Split(\"abc\", \",\", 0, 2))", "-1", 0)]
    [InlineData("Replace(\"aXb\", \"x\", \"-\", 1, -1, 2)", "a-b", 0)]
    [InlineData("Replace(\"\", \"a\", \"b\", 1, -1, 2)", "", 0)]
    [InlineData("Replace(\"abc\", \"a\", \"b\", 1, 0, 2)", "abc", 0)]
    [InlineData("StrComp(\"\", \"\", 2)", "0", 5)]
    [InlineData("StrComp(\"\", \"a\", 2)", "-1", 5)]
    [InlineData("StrComp(\"a\", \"a\", 2)", "0", 5)]
    [InlineData("StrComp(Null, \"a\", 2)", "Null", 0)]
    [InlineData("InStr(1, \"abc\", \"B\", -1)", "0", 5)]
    [InlineData("InStr(5, \"abc\", \"x\", -1)", "0", 5)]
    [InlineData("InStr(1, \"a\", \"long\", -1)", "0", 5)]
    [InlineData("InStr(1, Null, \"a\", -1)", "Null", 0)]
    [InlineData("InStr(1, \"\", \"a\", -1)", "0", 0)]
    [InlineData("InStr(5, \"abc\", \"\", -1)", "5", 0)]
    [InlineData("InStrRev(\"aXb\", \"x\", -1, -1)", "2", 5)]
    [InlineData("InStrRev(\"\", \"x\", -1, -1)", "0", 5)]
    [InlineData("UBound(Split(\"aXb\", \"x\", -1, -1))", "1", 5)]
    [InlineData("UBound(Split(\"\", \",\", -1, -1))", "-1", 5)]
    [InlineData("UBound(Split(\"abc\", \",\", 0, -1))", "-1", 5)]
    [InlineData("Replace(\"aXb\", \"x\", \"-\", 1, -1, -1)", "a-b", 5)]
    [InlineData("Replace(\"\", \"a\", \"b\", 1, -1, -1)", "", 5)]
    [InlineData("Replace(\"abc\", \"a\", \"b\", 1, 0, -1)", "abc", 5)]
    [InlineData("StrComp(\"\", \"\", -1)", "0", 0)]
    [InlineData("StrComp(\"\", \"a\", -1)", "-1", 0)]
    [InlineData("StrComp(\"a\", \"a\", -1)", "0", 5)]
    [InlineData("StrComp(Null, \"a\", -1)", "Null", 0)]
    [InlineData("StrComp(\"a\", \"A\", 0.4)", "1", 0)]
    [InlineData("StrComp(\"a\", \"A\", -0.4)", "1", 0)]
    [InlineData("StrComp(\"a\", \"A\", 0.6)", "0", 0)]
    [InlineData("StrComp(\"a\", \"A\", \"1\")", "0", 0)]
    [InlineData("InStr(1, \"abc\", \"B\", 1.5)", "0", 5)]
    [InlineData("StrComp(\"a\", \"A\", 1.5)", "0", 5)]
    [InlineData("UBound(Split(\"aXb\", \"x\", -1, 1.5))", "1", 0)]
    [InlineData("InStr(1, \"abc\", \"B\", 2.5)", "0", 5)]
    [InlineData("StrComp(\"a\", \"A\", 2.5)", "0", 5)]
    [InlineData("UBound(Split(\"aXb\", \"x\", -1, 2.5))", "1", 0)]
    [InlineData("InStr(1, \"abc\", \"B\", Null)", "0", 94)]
    [InlineData("InStrRev(\"abc\", \"B\", -1, Null)", "0", 94)]
    [InlineData("UBound(Split(\"abc\", \"B\", -1, Null))", "0", 94)]
    [InlineData("Replace(\"abc\", \"B\", \"x\", 1, -1, Null)", "0", 94)]
    [InlineData("StrComp(\"a\", \"A\", Null)", "0", 94)]
    [InlineData("InStr(1, Null, \"a\", 3000)", "Null", 0)]
    [InlineData("InStr(1, \"\", \"a\", 3000)", "0", 0)]
    [InlineData("InStr(5, \"abc\", \"\", 3000)", "5", 0)]
    [InlineData("StrComp(\"\", \"a\", 3000)", "-1", 0)]
    [InlineData("StrComp(Null, \"a\", 3000)", "Null", 0)]
    public async Task Compare_is_checked_at_the_measured_point(string expression, string expected, int error)
    {
        await Run($"""
            On Error Resume Next
            Dim result As Variant
            result = {expression}
            Debug.Print CStr(Err.Number)
            If Err.Number = 0 Then
                If IsNull(result) Then
                    Debug.Print "Null"
                Else
                    Debug.Print CStr(result)
                End If
            End If
            """);
        if (error == 0)
            AssertDebugLog([new Vb6Value("0"), new Vb6Value(expected)]);
        else
            AssertDebugLog([new Vb6Value(error.ToString())]);
    }

    [Theory]
    [InlineData("InStr(1, \"abc\", \"B\", 3)")]
    [InlineData("InStrRev(\"\", \"x\", -1, 3)")]
    [InlineData("Split(\"\", \",\", -1, 3)")]
    [InlineData("Split(\"abc\", \",\", 0, 3)")]
    [InlineData("Replace(\"\", \"a\", \"b\", 1, -1, 3)")]
    [InlineData("Replace(\"abc\", \"a\", \"b\", 1, 0, 3)")]
    [InlineData("StrComp(\"a\", \"a\", 3)")]
    [InlineData("InStr(1, \"abc\", \"B\", 4)")]
    [InlineData("InStrRev(\"\", \"x\", -1, 4)")]
    [InlineData("Split(\"\", \",\", -1, 4)")]
    [InlineData("Split(\"abc\", \",\", 0, 4)")]
    [InlineData("Replace(\"\", \"a\", \"b\", 1, -1, 4)")]
    [InlineData("Replace(\"abc\", \"a\", \"b\", 1, 0, 4)")]
    [InlineData("StrComp(\"a\", \"a\", 4)")]
    [InlineData("InStr(1, \"abc\", \"B\", 1033)")]
    [InlineData("InStrRev(\"\", \"x\", -1, 1033)")]
    [InlineData("Split(\"\", \",\", -1, 1033)")]
    [InlineData("Split(\"abc\", \",\", 0, 1033)")]
    [InlineData("Replace(\"\", \"a\", \"b\", 1, -1, 1033)")]
    [InlineData("Replace(\"abc\", \"a\", \"b\", 1, 0, 1033)")]
    [InlineData("StrComp(\"a\", \"a\", 1033)")]
    [InlineData("InStr(1, \"abc\", \"B\", 3000)")]
    [InlineData("InStrRev(\"\", \"x\", -1, 3000)")]
    [InlineData("Split(\"\", \",\", -1, 3000)")]
    [InlineData("Split(\"abc\", \",\", 0, 3000)")]
    [InlineData("Replace(\"\", \"a\", \"b\", 1, -1, 3000)")]
    [InlineData("Replace(\"abc\", \"a\", \"b\", 1, 0, 3000)")]
    [InlineData("StrComp(\"a\", \"a\", 3000)")]
    public async Task Locale_compare_is_refused_when_validation_is_needed(string expression)
    {
        var run = () => Run($"Dim result As Variant\nresult = {expression}\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*compare of*not supported*#881*");
    }
}
