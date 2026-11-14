using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class DefaultMemberExecutionTests : BaseVBTestFixture
{
    // Measured: an-object-used-as-a-value.md, cvar.md and
    // subscripts-bounds-and-a-method-called-from-class_initialize.md under docs/vb6-fidelity-oracle/ (#174).
    private const string Widget = "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\nValue = 42\nEnd Function\n";
    private const string Property = "Public Property Let Value(ByVal n As Long)\nAttribute Value.VB_UserMemId = 0\nEnd Property\nPublic Property Get Value() As Long\nValue = 42\nEnd Property\n";

    private Task Probe(string code, string source = Widget) => RunClasses(
        "Dim w As Widget, o, c\nSet w = New Widget\nSet o = w\n" + code,
        ("Widget", source));

    [Theory]
    [InlineData("Dim x As Long\nx = w\nDebug.Print x", "42")]
    [InlineData("Dim x As String\nx = w\nDebug.Print x", "42")]
    [InlineData("Dim x As Double\nx = w\nDebug.Print x", "42")]
    [InlineData("Dim x As Boolean\nx = w\nDebug.Print x", "True")]
    [InlineData("c = w\nDebug.Print c", "42")]
    [InlineData("Debug.Print w + 1", "43")]
    [InlineData("Debug.Print w * 2", "84")]
    [InlineData("Debug.Print -w", "-42")]
    [InlineData("Debug.Print w \\ 5", "8")]
    [InlineData("Debug.Print w = 42", "True")]
    [InlineData("Debug.Print w > 41", "True")]
    [InlineData("Debug.Print \"v=\" & w", "v=42")]
    [InlineData("Debug.Print CStr(w)", "42")]
    [InlineData("Debug.Print CDbl(w)", "42")]
    [InlineData("Debug.Print CBool(w)", "True")]
    [InlineData("Debug.Print Val(w)", "42")]
    [InlineData("Debug.Print CLng(w)", "42")]
    [InlineData("Debug.Print RGB(w, 0, 0)", "42")]
    [InlineData("If w Then c = 1\nDebug.Print c", "1")]
    [InlineData("If False Then\nc = 0\nElseIf w Then\nc = 1\nEnd If\nDebug.Print c", "1")]
    [InlineData("Select Case w\nCase 42\nc = 1\nEnd Select\nDebug.Print c", "1")]
    [InlineData("Select Case 43\nCase Is > w\nc = 1\nEnd Select\nDebug.Print c", "1")]
    [InlineData("Select Case 42\nCase 40 To w\nc = 1\nEnd Select\nDebug.Print c", "1")]
    [InlineData("Select Case 42\nCase w To 50\nc = 1\nEnd Select\nDebug.Print c", "1")]
    [InlineData("Dim i As Long, n As Long\nFor i = 40 To w\nn = n + 1\nNext\nDebug.Print n", "3")]
    [InlineData("Dim i As Long, n As Long\nFor i = w To 45\nn = n + 1\nNext\nDebug.Print n", "4")]
    [InlineData("Dim i As Long, n As Long\nFor i = 1 To 100 Step w\nn = n + 1\nNext\nDebug.Print n", "3")]
    [InlineData("Debug.Print Left(o, 1)", "4")]
    [InlineData("Debug.Print InStr(o, \"2\")", "2")]
    [InlineData("Debug.Print Chr(o)", "*")]
    [InlineData("Debug.Print Len(o)", "2")]
    [InlineData("Debug.Print UCase(o)", "42")]
    [InlineData("Debug.Print IIf(o, \"t\", \"f\")", "t")]
    [InlineData("Debug.Print VarType(w)", "3")]
    [InlineData("Debug.Print IsNumeric(w)", "True")]
    [InlineData("Debug.Print Not w", "-43")]
    [InlineData("Debug.Print CDbl(CDate(w))", "42")]
    [InlineData("Debug.Print CDbl(CDate(o))", "42")]
    [InlineData("Debug.Print VarType(CVar(w))", "3")]
    public async Task Measured_value_uses_read_the_member(string code, string expected)
    {
        // CStr of captured results avoids pinning the inferred Debug.Print object output.
        await Probe(code.Replace("Debug.Print ", "c = ").ReplaceLineEndings("\n") + "\nDebug.Print CStr(c)\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    // Pinned divergence: #174's round-four measurements distinguish early/late binding here.
    // Until #211 handles mixed String comparisons, refuse rather than return a wrong value or trappable 13.
    [Theory]
    [InlineData("Long", "w", "=", "42")]
    [InlineData("Long", "w", ">", "5")]
    [InlineData("Long", "w", "=", "42.0")]
    [InlineData("Long", "w", "=", "abc")]
    [InlineData("Integer", "w", "=", "42")]
    [InlineData("Integer", "w", ">", "5")]
    [InlineData("Double", "w", ">", "5")]
    [InlineData("Property", "w", ">", "5")]
    [InlineData("Property", "w", "=", "abc")]
    [InlineData("Variant", "w", ">", "5")]
    [InlineData("Variant", "w", "=", "abc")]
    [InlineData("Long", "o", ">", "5")]
    [InlineData("Long", "o", "=", "abc")]
    [InlineData("String", "w", "=", "42")]
    [InlineData("String", "w", ">", "5")]
    public async Task Mixed_String_default_comparisons_refuse_in_both_orders(
        string type, string variable, string comparison, string operand)
    {
        var source = type == "Property" ? Property : Widget.Replace("As Long", "As " + type);
        if (type == "String")
            source = source.Replace("Value = 42", "Value = \"42\"");
        var other = type == "String" ? operand : "\"" + operand + "\"";
        foreach (var expression in new[] { $"{variable} {comparison} {other}", $"{other} {comparison} {variable}" })
        {
            var run = () => Probe($"On Error Resume Next\nc = {expression}\n", source);
            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#211*#174*");
        }
    }

    [Fact]
    public async Task String_default_compares_with_a_String()
    {
        // #174, round four: both String operands compare lexically.
        await Probe("c = w > \"5\"\nDebug.Print c\n", Widget.Replace("As Long", "As String").Replace("Value = 42", "Value = \"42\""));
        AssertDebugLog([new Vb6Value(false)]);
    }

    [Theory]
    [InlineData("Format(o)")]
    [InlineData("Format(o, \"0.0\")")]
    [InlineData("Abs(o)")]
    [InlineData("Not o")]
    public async Task Function_through_Variant_gives_450(string expression)
    {
        await Probe($"On Error Resume Next\nc = {expression}\nDebug.Print Err.Number\nDebug.Print IsEmpty(c)\n");
        AssertDebugLog([new Vb6Value(450L), new Vb6Value(true)]);
    }

    [Theory]
    [InlineData("Format(o)", "42")]
    [InlineData("Abs(o)", "42")]
    [InlineData("Not o", "-43")]
    [InlineData("w + 1", "43")]
    [InlineData("\"v=\" & w", "v=42")]
    public async Task Property_Get_reads_through_a_Variant_and_a_Let_attribute(string expression, string expected)
    {
        // Attribute placement: attribute-lines-inside-a-procedure.md and #174.
        await Probe($"c = {expression}\nDebug.Print CStr(c)\n", Property);
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Empty", "w")]
    [InlineData("Null", "w")]
    [InlineData("Empty", "o")]
    [InlineData("Null", "o")]
    public async Task Function_Case_against_Empty_or_Null_gives_the_IDEs_450(string selector, string expression)
    {
        await Probe($"On Error Resume Next\nSelect Case {selector}\nCase {expression}\nc = 1\nEnd Select\nDebug.Print Err.Number\nDebug.Print c\n");
        AssertDebugLog([new Vb6Value(450L), new Vb6Value((short)1)]);
    }

    [Theory]
    [InlineData("Long", "42")]
    [InlineData("String", "42")]
    [InlineData("Double", "42")]
    [InlineData("Variant", "Widget")]
    public async Task ByVal_scalar_reads_the_member_and_Variant_takes_the_object(string type, string expected)
    {
        var result = type == "Variant" ? "TypeName(n)" : "CStr(n)";
        await Probe($"P w\nSub P(ByVal n As {type})\nDebug.Print {result}\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Err.Raise w", 42L)]
    [InlineData("Error w", 42L)]
    [InlineData("ChDir w", 76L)]
    [InlineData("ChDrive w", 5L)]
    public async Task Statements_read_the_member(string statement, long error)
    {
        var setup = statement == "ChDir w" ? $"ChDir \"{Directory.GetCurrentDirectory()}\"\n" : "";
        await Probe(setup + $"On Error Resume Next\n{statement}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(error)]);
    }

    [Fact]
    public async Task CVar_keeps_the_object_until_a_value_is_wanted()
    {
        // cvar.md specifically measures K1's Integer 7 default, overriding the older general table.
        await RunClasses("Dim k As K1\nSet k = New K1\nDebug.Print TypeName(CVar(k))\nDebug.Print IsObject(CVar(k))\nDebug.Print CVar(k) + 1\nDebug.Print VarType(CVar(k))\n",
            ("K1", "Public Function Value() As Integer\nAttribute Value.VB_UserMemId = 0\nValue = 7\nEnd Function\n"));
        AssertDebugLog([new Vb6Value("K1"), new Vb6Value(true), new Vb6Value((short)8), new Vb6Value(2L)]);
    }

    [Theory]
    [InlineData("IsEmpty", "Empty", "0")]
    [InlineData("IsNull", "Null", "1")]
    [InlineData("IsDate", "#1/2/2000#", "7")]
    [InlineData("IsNumeric", "\"12\"", "8")]
    [InlineData("IsArray", "Array(1)", "8204")]
    [InlineData("IsError", "CVErr(5)", "10")]
    public async Task Inspection_functions_read_the_default_result(string function, string result, string type)
    {
        await Probe($"Debug.Print {function}(w)\nDebug.Print CStr(VarType(w))\n",
            $"Public Function Value() As Variant\nAttribute Value.VB_UserMemId = 0\nValue = {result}\nEnd Function\n");
        AssertDebugLog([new Vb6Value(true), new Vb6Value(type)]);
    }

    [Theory]
    [InlineData("If w Then c = 1")]
    [InlineData("If w Then\nc = 1\nEnd If")]
    [InlineData("Do While w\nc = 1\nExit Do\nLoop")]
    [InlineData("Do\nc = 1\nLoop Until w")]
    public async Task Conditions_read_the_member(string statement)
    {
        await Probe(statement + "\nDebug.Print c\n");
        AssertDebugLog([new Vb6Value((short)1)]);
    }

    [Fact]
    public async Task Small_default_drives_Error_and_Mid_statement_arguments()
    {
        await Probe("Dim s As String\ns = \"abcdef\"\nMid(s, w, 1) = \"X\"\nDebug.Print s\ns = \"abcdef\"\nMid(s, 2, w) = \"XYZ\"\nDebug.Print s\nOn Error Resume Next\nError w\nDebug.Print Err.Number\n", Widget.Replace("42", "2"));
        AssertDebugLog([new Vb6Value("aXcdef"), new Vb6Value("aXYdef"), new Vb6Value(2L)]);
    }

    [Fact]
    public async Task Randomize_uses_the_default_as_its_seed()
    {
        await Probe("Dim a As Single, b As Single\na = Rnd(-1)\nRandomize 42\na = Rnd\nb = Rnd(-1)\nRandomize w\nb = Rnd\nDebug.Print a = b\n");
        AssertDebugLog([new Vb6Value(true)]);
    }

    [Fact]
    public async Task Print_file_reads_the_member()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            await Probe($"Open \"{path}\" For Output As #1\nPrint #1, w\nClose #1\n");
            File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(" 42 \n");
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("Empty")]
    [InlineData("Null")]
    public async Task Property_Case_against_Empty_or_Null_takes_Case_Else(string selector)
    {
        await Probe($"Select Case {selector}\nCase o\nc = 1\nCase Else\nc = 2\nEnd Select\nDebug.Print c\n", Property);
        AssertDebugLog([new Vb6Value((short)2)]);
    }

    [Fact]
    public async Task Default_values_keep_the_measured_Long_subtype()
    {
        await Probe("c = w\nDebug.Print VarType(c)\nc = Not w\nDebug.Print c\nDebug.Print VarType(c)\nc = w + 1\nDebug.Print c\nDebug.Print VarType(c)\nDebug.Print TypeName(CDate(w))\n");
        AssertDebugLog([new Vb6Value(3L), new Vb6Value(-43L), new Vb6Value(3L), new Vb6Value(43L), new Vb6Value(3L), new Vb6Value("Date")]);
    }

    [Theory]
    [InlineData("UBound(m, w)", "9")]
    [InlineData("LBound(m, w)", "5")]
    [InlineData("UBound(Split(\"a,b,c\", \",\", w))", "1")]
    public async Task Small_default_reads_as_a_dimension_or_count(string expression, string expected)
    {
        await Probe($"Dim m(1 To 3, 5 To 9)\nc = {expression}\nDebug.Print CStr(c)\n", Widget.Replace("42", "2"));
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Theory]
    [InlineData("Sub", "Public Sub Value()\nAttribute Value.VB_UserMemId = 0\nEnd Sub\n")]
    [InlineData("argument", "Public Function Value(n As Long) As Long\nAttribute Value.VB_UserMemId = 0\nEnd Function\n")]
    [InlineData("nested", "Public Function Value() As Object\nAttribute Value.VB_UserMemId = 0\nSet Value = Me\nEnd Function\n")]
    [InlineData("in-box object", "Public Function Value() As Object\nAttribute Value.VB_UserMemId = 0\nSet Value = Err\nEnd Function\n")]
    public async Task Unmeasured_members_refuse_untrappably(string shape, string source)
    {
        _ = shape;
        var run = () => Probe("On Error Resume Next\nc = w\n", source);
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }

    [Theory]
    [InlineData("Format(w)")]
    [InlineData("Abs(w)")]
    [InlineData("Format(42, o)")]
    public async Task Unmeasured_Function_uses_stay_refused(string expression)
    {
        var run = () => Probe($"On Error Resume Next\nc = {expression}\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#174*");
    }
}
