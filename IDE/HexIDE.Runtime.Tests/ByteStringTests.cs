using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The byte-string functions (#1510): measured values come from vb6.exe SP6, native and p-code alike,
/// docs/vb6-fidelity-oracle/the-byte-string-functions.md. Conversion support and refusal policy follow #4576.
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

    // #4576, measured rows from the-byte-string-functions.md. Read bytes without displaying odd text.
    [Theory]
    [InlineData("ChrB(65)", "41")]
    [InlineData("ChrB(255)", "FF")]
    [InlineData("ChrB(CInt(66))", "42")]
    [InlineData("ChrB(CLng(66))", "42")]
    [InlineData("ChrB(CByte(66))", "42")]
    [InlineData("ChrB(CSng(66))", "42")]
    [InlineData("ChrB(CDbl(66))", "42")]
    [InlineData("ChrB(CCur(66))", "42")]
    [InlineData("ChrB(65.5)", "42")]
    [InlineData("ChrB(66.5)", "42")]
    [InlineData("ChrB(\"66\")", "42")]
    [InlineData("MidB(\"abc\", 2, 3)", "006200")]
    [InlineData("MidB(\"abc\", 2)", "0062006300")]
    [InlineData("MidB(\"abc\", 6)", "00")]
    [InlineData("LeftB(\"abc\", 3)", "610062")]
    [InlineData("RightB(\"abc\", 3)", "006300")]
    [InlineData("RightB(\"abc\", 1)", "00")]
    [InlineData("ChrB(65) & \"B\"", "414200")]
    [InlineData("ChrB(65) & Empty", "41")]
    [InlineData("Empty & ChrB(65)", "41")]
    [InlineData("ChrB(65) & Null", "41")]
    [InlineData("Null & ChrB(65)", "41")]
    public async Task Odd_results_keep_the_measured_bytes(string expression, string hex)
    {
        await Run($"Dim r\nr = {expression}\n");
        context.TryGetVariable(rootEnv, "r", out var value).Should().BeTrue();
        Convert.ToHexString((byte[])value.Value!).Should().Be(hex);
    }

    [Theory]
    [InlineData("TypeName(ChrB(65))", "String")]
    [InlineData("VarType(ChrB(65))", "8")]
    [InlineData("LenB(ChrB(65))", "1")]
    [InlineData("Len(ChrB(65))", "0")]
    [InlineData("Len(ChrB(65) & \"B\")", "1")]
    [InlineData("ChrB(65) & ChrB(0) = \"A\"", "True")]
    [InlineData("InStrB(ChrB(1) & \"a\", \"a\")", "2")]
    [InlineData("InStrB(\"abc\", ChrB(0))", "2")]
    [InlineData("ChrB(65) = \"A\"", "False")]
    [InlineData("ChrB(65) < \"A\"", "True")]
    public async Task Odd_strings_support_the_measured_uses(string expression, string expected)
    {
        (await Printed(expression)).Should().Be(expected);
    }

    [Theory]
    [InlineData("ChrB(256)", 6)]
    [InlineData("ChrB(-1)", 6)]
    [InlineData("ChrB(Null)", 94)]
    public async Task ChrB_reports_the_measured_error(string expression, int error)
    {
        await A_byte_string_function_raises_what_vb6_raised(expression, error);
    }

    [Theory]
    [InlineData("Dim s As String\ns = ChrB(65)\nDebug.Print LenB(s)")]
    [InlineData("Dim s As Variant\ns = ChrB(65)\nDebug.Print LenB(s)")]
    public async Task A_String_or_Variant_variable_keeps_the_odd_byte(string code)
    {
        await Run(code);
        debug.Single().Value.Should().Be(1L);
    }

    [Theory]
    [InlineData("Dim r\nr = ChrB(True)", "ChrB argument conversion")]
    [InlineData("Dim r\nr = MidB(ChrB(65), 1)", "MidB")]
    [InlineData("Dim r\nr = LeftB(ChrB(65), 1)", "LeftB")]
    [InlineData("Dim r\nr = RightB(ChrB(65), 1)", "RightB")]
    [InlineData("Dim r\nr = ChrB(65) & 1", "Concatenation")]
    [InlineData("Dim r\nr = ChrB(65) <> \"A\"", "<>")]
    [InlineData("Dim r\nr = ChrB(65) > \"A\"", ">")]
    [InlineData("Dim r\nr = ChrB(65) <= \"A\"", "<=")]
    [InlineData("Dim r\nr = ChrB(65) >= \"A\"", ">=")]
    [InlineData("Option Compare Text\nDim r\nr = ChrB(65) = \"A\"", "Option Compare Text")]
    [InlineData("Dim r\nr = StrComp(ChrB(65), \"A\")", "StrComp")]
    [InlineData("Dim r\nr = ChrB(65) Like \"A\"", "Like")]
    [InlineData("Dim r As Long\nr = ChrB(65)", "Assignment to Long")]
    [InlineData("Debug.Print ChrB(65)", "Debug.Print")]
    [InlineData("Dim r\nr = UCase(ChrB(65))", "UCase")]
    [InlineData("Dim r\nr = CStr(ChrB(65))", "CStr")]
    [InlineData("Dim r\nr = AscB(ChrB(65))", "AscB")]
    [InlineData("Dim r\nr = MsgBox(ChrB(65))", "MsgBox")]
    [InlineData("Dim r\nr = ChrB(Empty)", "ChrB argument conversion")]
    [InlineData("Dim r\nr = ChrB(#1/1/2000#)", "ChrB argument conversion")]
    [InlineData("Dim r\nr = ChrB(Array(65))", "ChrB argument conversion")]
    [InlineData("Dim r\nr = ChrB(New Collection)", "ChrB argument conversion")]
    [InlineData("Dim r\nr = ChrB(CDec(65))", "ChrB argument conversion")]
    [InlineData("Dim c As New Collection\nDim r\nr = ChrB(65) & c", "Concatenation")]
    [InlineData("Dim r\nr = ChrB(65) + Null", "AddOrSubtract")]
    [InlineData("Dim r\nr = ChrB(65) - 1", "AddOrSubtract")]
    [InlineData("Dim r\nr = ChrB(65) * 1", "MultiplyOrDivide")]
    [InlineData("Dim r\nr = ChrB(65) / 1", "MultiplyOrDivide")]
    [InlineData("Dim r\nr = ChrB(65) Mod 1", "Modulo")]
    [InlineData("Dim r\nr = ChrB(65) ^ 1", "Power")]
    [InlineData("Dim r\nr = ChrB(65) And 1", "Bitwise operation")]
    [InlineData("If ChrB(65) Then Debug.Print 1", "Condition")]
    [InlineData("Select Case ChrB(65)\nCase Else\nEnd Select", "Select Case")]
    [InlineData("Dim s As String * 5\ns = ChrB(65)", "FixedLengthString")]
    [InlineData("Dim r\nr = IIf(True, ChrB(65), Empty)", "IIf")]
    [InlineData("Dim s As String\ns = \"abc\"\nMidB(s, 1) = ChrB(65)", "MidB statement")]
    [InlineData("Dim r\nr = Not ChrB(65)", "Not")]
    [InlineData("Dim r\nr = -ChrB(65)", "Negation")]
    [InlineData("Dim b() As Byte\nb = ChrB(65)", "Array assignment")]
    [InlineData("Dim r\nr = StrPtr(ChrB(65))", "StrPtr")]
    [InlineData("Dim r\nr = VarPtr(ChrB(65))", "VarPtr")]
    [InlineData("Dim r\nr = IsNumeric(ChrB(65))", "IsNumeric")]
    [InlineData("Dim r\nr = TypeOf ChrB(65) Is Object", "TypeOf")]
    [InlineData("Dim r\nr = ChrB(65) Is Nothing", "Is")]
    [InlineData("Dim r\nr = ChrB(65) \\ 1", "IntegerDivide")]
    [InlineData("Dim c As New Collection\nDim r\nr = CallByName(c, ChrB(65), 2)", "CallByName")]
    [InlineData("Dim r\nr = CreateObject(ChrB(65))", "CreateObject")]
    public async Task Other_uses_refuse_at_the_named_operation(string code, string operation)
    {
        var run = () => Run(code + "\n");
        var exception = (await run.Should().ThrowAsync<NotImplementedException>()).Which;
        exception.Message.Should().Contain(operation).And.Contain("#1510");
    }

    [Fact]
    public async Task Two_odd_Strings_join_into_an_ordinary_String()
    {
        await Run("Dim r\nr = ChrB(65) & ChrB(0)\n");
        context.TryGetVariable(rootEnv, "r", out var value).Should().BeTrue();
        value.Type.Should().Be(Vb6Value.ValueType.String);
        value.Value.Should().Be("A");
    }

    [Fact]
    public async Task Text_display_refuses_explicitly()
    {
        await Run("Dim r\nr = ChrB(65)\n");
        context.TryGetVariable(rootEnv, "r", out var value).Should().BeTrue();
        var display = () => VbDisplay.ToText(value);
        display.Should().Throw<NotImplementedException>().WithMessage("*Display*#1510*");
    }

    [Theory]
    [InlineData("Output", "Print #1, r", "Print #")]
    [InlineData("Output", "Write #1, r", "Write #")]
    [InlineData("Binary", "Put #1, , r", "Put")]
    public async Task File_output_refuses_at_the_output_operation(string mode, string statement, string operation)
    {
        var path = Path.GetTempFileName();
        try
        {
            var code = $"Dim r\nr = ChrB(65)\nOpen \"{path}\" For {mode} As #1\n{statement}\n";
            var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code);
            try
            {
                var run = () => vb.Execute();
                var exception = (await run.Should().ThrowAsync<NotImplementedException>()).Which;
                exception.Message.Should().Contain(operation).And.Contain("#1510");
            }
            finally
            {
                vb.BuiltIns.Files.CloseAll();
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("c & \"x\"", "Concatenation")]
    [InlineData("\"x\" & c", "Concatenation")]
    [InlineData("c = \"A\"", "Comparison")]
    [InlineData("ChrB(65) = c", "Comparison")]
    public async Task An_odd_default_member_does_not_enable_unmeasured_object_operators(string expression, string operation)
    {
        const string widget = "Public Property Get Value() As String\nAttribute Value.VB_UserMemId = 0\nValue = ChrB(65)\nEnd Property\n";
        var run = () => RunClasses($"Dim c As New Widget\nDim r\nr = {expression}\n", ("Widget", widget));
        var exception = (await run.Should().ThrowAsync<NotImplementedException>()).Which;
        exception.Message.Should().Contain(operation).And.Contain("#1510");
    }

    [Fact]
    public async Task ChrB_refuses_an_object_before_reading_its_default_member()
    {
        const string widget = "Public Property Get Value() As Long\nAttribute Value.VB_UserMemId = 0\nValue = 66\nEnd Property\n";
        var run = () => RunClasses("Dim c As New Widget\nDim r\nr = ChrB(c)\n", ("Widget", widget));
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*ChrB argument conversion*#1510*");
    }

    [Theory]
    [InlineData("ByVal x As String")]
    [InlineData("ByRef x As String")]
    [InlineData("ByVal x As Variant")]
    [InlineData("ParamArray x() As Variant")]
    public async Task User_procedure_arguments_remain_explicitly_unimplemented(string parameter)
    {
        var run = () => Run($"Sub Take({parameter})\nEnd Sub\nDim s As String\ns = ChrB(65)\nTake s\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Argument to Take*#1510*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("c.Add \"value\"\n")]
    public async Task Collection_indexing_refuses_before_the_empty_or_numeric_paths(string initialise)
    {
        var run = () => Run($"Dim c As New Collection\nDim r\n{initialise}r = c(ChrB(65))\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Collection.Item*#1510*");
    }

    [Fact]
    public async Task An_even_join_retains_the_existing_fixed_String_expression_metadata()
    {
        await Run("Dim x As String, y As String\nx = ChrB(65)\ny = ChrB(0)\n");
        context.TryGetVariable(rootEnv, "x", out var x).Should().BeTrue();
        context.TryGetVariable(rootEnv, "y", out var y).Should().BeTrue();
        x.HasFixedType.Should().BeTrue();
        y.HasFixedType.Should().BeTrue();
        // This is the existing expression metadata contract, not another VB6 measurement.
        var joined = ExpressionExecutor.Concatenate(x, y);
        joined.Value.Should().Be("A");
        joined.HasFixedType.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_odd_join_keeps_the_existing_Null_concatenation_metadata(bool nullOnLeft)
    {
        await Run("Dim r As Variant\nr = ChrB(65)\n");
        context.TryGetVariable(rootEnv, "r", out var odd).Should().BeTrue();
        odd.HeldAsVariant.Should().BeTrue();
        var ordinary = new Vb6Value("A").AsVariant();
        // Preserve the ordinary branch's internal metadata policy, without claiming a new oracle row.
        var expected = nullOnLeft ? ExpressionExecutor.Concatenate(Vb6Value.Null, ordinary)
            : ExpressionExecutor.Concatenate(ordinary, Vb6Value.Null);
        var actual = nullOnLeft ? ExpressionExecutor.Concatenate(Vb6Value.Null, odd)
            : ExpressionExecutor.Concatenate(odd, Vb6Value.Null);
        actual.HasFixedType.Should().Be(expected.HasFixedType);
        actual.HeldAsVariant.Should().Be(expected.HeldAsVariant);
    }

    [Theory]
    [InlineData("StrConv(\"A\", vbFromUnicode)", "StrConv")]
    public async Task Deferred_byte_conversions_do_not_claim_the_representation_is_missing(string expression, string operation)
    {
        var run = () => Run($"Dim r\nr = {expression}\n");
        var exception = (await run.Should().ThrowAsync<NotImplementedException>()).Which;
        exception.Message.Should().Contain(operation).And.Contain("not implemented").And.Contain("#1510")
            .And.NotContain("has not got").And.NotContain("does not have");
    }

    [Fact]
    public async Task Byte_array_to_String_assignment_remains_an_explicit_gap()
    {
        var run = () => Run("On Error Resume Next\nDim b(2) As Byte\nb(0) = 65: b(1) = 66: b(2) = 67\nDim s As String\ns = b\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*Byte-array-to-String*#1510*");
    }
}
