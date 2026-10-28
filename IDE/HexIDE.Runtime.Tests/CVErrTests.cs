using System.IO;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/2520.
public class CVErrTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("5", "Error 5")]
    [InlineData("0", "Error 0")]
    [InlineData("2042", "Error 2042")]
    [InlineData("65535", "Error 65535")]
    [InlineData("1.5", "Error 2")]
    [InlineData("2.5", "Error 2")]
    [InlineData("\"7\"", "Error 7")]
    [InlineData("Empty", "Error 0")]
    [InlineData("CVErr(3)", "Error 3")]
    public async Task Construction(string argument, string expected)
    {
        await Run($"""
            Dim e
            e = CVErr({argument})
            Debug.Print CStr(e)
            Debug.Print TypeName(e)
            Debug.Print Err.Number
            """);
        AssertDebugLog([expected, "Error", new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData("65536", 5)]
    [InlineData("-1", 5)]
    [InlineData("True", 5)]
    [InlineData("\"abc\"", 13)]
    [InlineData("Null", 94)]
    public async Task Invalid_argument(string argument, int error)
    {
        await Run($"""
            On Error Resume Next
            Dim e
            e = CVErr({argument})
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value((long)error)]);
    }

    [Theory]
    [InlineData("IsError(e)", "True", "Boolean")]
    [InlineData("VarType(e)", "10", "Long")]
    [InlineData("TypeName(e)", "Error", "String")]
    [InlineData("CStr(e)", "Error 5", "String")]
    [InlineData("CLng(e)", "5", "Long")]
    [InlineData("CInt(e)", "5", "Integer")]
    [InlineData("CDbl(e)", "5", "Double")]
    [InlineData("e = CVErr(5)", "True", "Boolean")]
    [InlineData("e = CVErr(6)", "False", "Boolean")]
    [InlineData("IsNumeric(e)", "False", "Boolean")]
    [InlineData("IsEmpty(e)", "False", "Boolean")]
    [InlineData("IsNull(e)", "False", "Boolean")]
    [InlineData("e < CVErr(6)", "True", "Boolean")]
    [InlineData("e > CVErr(4)", "True", "Boolean")]
    [InlineData("e <> CVErr(6)", "True", "Boolean")]
    [InlineData("e <= CVErr(5)", "True", "Boolean")]
    [InlineData("CBool(e)", "True", "Boolean")]
    [InlineData("CByte(e)", "5", "Byte")]
    [InlineData("CSng(e)", "5", "Single")]
    [InlineData("CCur(e)", "5", "Currency")]
    [InlineData("CVar(e)", "Error 5", "Error")]
    [InlineData("IIf(True, e, 1)", "Error 5", "Error")]
    public async Task Inspection_and_conversion(string expression, string expected, string type)
    {
        await Run($"""
            Dim e, result
            e = CVErr(5)
            result = {expression}
            Debug.Print CStr(result)
            Debug.Print TypeName(result)
            Debug.Print Err.Number
            """);
        AssertDebugLog([expected, type, new Vb6Value(0L)]);
    }

    [Theory]
    [InlineData("e + 1")]
    [InlineData("e & \"x\"")]
    [InlineData("e = 5")]
    [InlineData("Len(e)")]
    [InlineData("Format(e)")]
    [InlineData("Not e")]
    [InlineData("-e")]
    [InlineData("Int(e)")]
    [InlineData("Fix(e)")]
    [InlineData("Abs(e)")]
    [InlineData("Sgn(e)")]
    [InlineData("Val(e)")]
    [InlineData("Str(e)")]
    [InlineData("Hex(e)")]
    [InlineData("CDec(e)")]
    [InlineData("CDate(e)")]
    [InlineData("e = Null")]
    [InlineData("e = Empty")]
    [InlineData("e = \"5\"")]
    [InlineData("e = \"abc\"")]
    [InlineData("e < \"5\"")]
    public async Task Implicit_use_is_type_mismatch(string expression)
    {
        await Run($"""
            Dim e, result
            e = CVErr(5)
            On Error Resume Next
            result = {expression}
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Theory]
    [InlineData("Long", "0")]
    [InlineData("String", "")]
    public async Task Assignment_refuses_and_preserves_destination(string type, string initial)
    {
        await Run($"""
            Dim e, destination As {type}
            e = CVErr(5)
            On Error Resume Next
            destination = e
            Debug.Print Err.Number
            Debug.Print CStr(destination)
            """);
        AssertDebugLog([new Vb6Value(13L), initial]);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("Empty")]
    public async Task Ordinary_values_are_not_errors(string expression)
    {
        await Run($"Debug.Print IsError({expression})");
        AssertDebugLog([false]);
    }

    [Theory]
    [InlineData("CVDate(e)")]
    public async Task Unmeasured_conversions_refuse(string expression)
    {
        var run = () => Run($"""
            Dim e, result
            e = CVErr(5)
            On Error Resume Next
            result = {expression}
            """);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message
            .Should().Contain("conversion of an Error Variant is unmeasured");
    }

    [Theory]
    [InlineData("Byte")]
    [InlineData("Integer")]
    [InlineData("Single")]
    [InlineData("Double")]
    [InlineData("Currency")]
    [InlineData("Boolean")]
    [InlineData("Date")]
    public async Task Unmeasured_declared_stores_refuse(string type)
    {
        var run = () => Run($"""
            Dim e, destination As {type}
            e = CVErr(5)
            On Error Resume Next
            destination = e
            """);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message
            .Should().Contain("Assignment of an Error Variant");
    }

    // Existing measurements: subscripts-bounds-and-a-method-called-from-class_initialize.md.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Error_is_not_an_index(bool controlArray)
    {
        VB6BuiltIns.TryGetBuiltIn("CVErr", out var factory).Should().BeTrue();
        var value = factory!(null!, [new Vb6Value(5)], false);
        Action read = () =>
        {
            if (controlArray) VB6Visitor<object>.ControlArrayIndex(value);
            else VB6Visitor<object>.Subscript(value);
        };
        read.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(13);
    }

    // Existing measurement: what-a-condition-is.md.
    [Fact]
    public async Task Error_is_not_a_condition()
    {
        var run = () => Run("""
            Dim e
            e = CVErr(5)
            If e Then Debug.Print "body"
            """);
        (await run.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(13);
    }

    // Follow-up measurements: #2520 comments and measuring PR #2613, native and p-code.
    [Theory]
    [InlineData("5", 13)]
    [InlineData("\"5\"", 13)]
    [InlineData("\"abc\"", 13)]
    [InlineData("Null", 13)]
    [InlineData("Empty", 13)]
    [InlineData("Is > 4", 13)]
    [InlineData("1 To 9", 13)]
    [InlineData("CVErr(5)", 0)]
    [InlineData("Is > CVErr(4)", 0)]
    public async Task Select_case_runs_the_measured_arm(string test, long error)
    {
        await Run($"""
            On Error Resume Next
            Dim e
            e = CVErr(5)
            Select Case e
            Case {test}
                Debug.Print "case"
                Debug.Print Err.Number
            Case Else
                Debug.Print "else"
            End Select
            Debug.Print Err.Number
            """);
        AssertDebugLog(["case", new Vb6Value(error), new Vb6Value(error)]);
    }

    // The other way round: a selector of another type against an Error Case value (#2520, native and p-code).
    [Theory]
    [InlineData("Dim n", "n = 5", "Case e")]
    [InlineData("Dim n As Long", "n = 5", "Case Is < e")]
    [InlineData("Dim n As String", "n = \"5\"", "Case e To e")]
    [InlineData("Dim n", "n = \"5\"", "Case e")]
    [InlineData("Dim n", "n = Null", "Case Is < e")]
    [InlineData("Dim n", "n = Empty", "Case e To e")]
    public async Task A_selector_against_an_Error_case_value_is_type_mismatch(string declaration, string store, string test)
    {
        await Run($"""
            On Error Resume Next
            Dim e
            {declaration}
            e = CVErr(5)
            {store}
            Select Case n
            {test}
                Debug.Print "case"
                Debug.Print Err.Number
            Case Else
                Debug.Print "else"
            End Select
            """);
        AssertDebugLog(["case", new Vb6Value(13L)]);
    }

    [Fact]
    public async Task If_error_resumes_in_then_body()
    {
        await Run("""
            On Error Resume Next
            Dim e
            e = CVErr(5)
            If e Then
                Debug.Print "then"
            Else
                Debug.Print "else"
            End If
            Debug.Print Err.Number
            """);
        AssertDebugLog(["then", new Vb6Value(13L)]);
    }

    [Fact]
    public async Task Is_requires_an_object()
    {
        await Run("""
            On Error Resume Next
            Dim e, result
            e = CVErr(5)
            result = e Is Nothing
            Debug.Print Err.Number
            """);
        AssertDebugLog([new Vb6Value(424L)]);
    }

    [Theory]
    [InlineData("Print #f, e", "Error 5")]
    [InlineData("Print #f, \"a\"; e; \"b\"", "aError 5b")]
    [InlineData("Print #f, \"a\", e, \"b\"", "a             Error 5       b")]
    [InlineData("Write #f, e", "#ERROR 5#")]
    [InlineData("Write #f, \"a\", e, \"b\"", "\"a\",#ERROR 5#,\"b\"")]
    public async Task File_output(string statement, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hexide-cverr-{Guid.NewGuid():N}.txt");
        try
        {
            await Run($"""
                Dim e, f As Integer
                e = CVErr(5)
                f = FreeFile
                Open "{path}" For Output As #f
                {statement}
                Close #f
                """);
            File.ReadAllText(path).Should().Be(expected + "\r\n");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Debug_print_displays_error()
    {
        await Run("Debug.Print CVErr(5)");
        written.ToString().Should().Be("Error 5\r\n");
    }

    [Fact]
    public async Task MsgBox_displays_error()
    {
        await Run("MsgBox CVErr(5)");
        dialogs.Should().ContainSingle().Which.Should().Be("MsgBox Error 5 |  | Ok");
    }
}
