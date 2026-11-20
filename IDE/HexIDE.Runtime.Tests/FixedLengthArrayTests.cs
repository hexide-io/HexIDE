using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 native/p-code rows from #179's brief and its review (oracle: fixed-length-strings.md, "Arrays of
// fixed-length Strings, the width and parameters").
public class FixedLengthArrayTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("", "2.6", 3)]
    [InlineData("", "2.5", 2)]
    [InlineData("", "3.5", 4)]
    [InlineData("As Byte", "3", 3)]
    [InlineData("As Currency", "3", 3)]
    [InlineData("As Double", "3", 3)]
    [InlineData("As Single", "3", 3)]
    [InlineData("As Date", "3", 3)]
    [InlineData("", "\"4\"", 4)]
    [InlineData("", "\"2.5\"", 2)]
    [InlineData("As Variant", "3", 3)]
    public async Task Constant_width_rounds_to_even(string type, string value, int width)
    {
        await Run($"Const W {type} = {value}\nDim x As String * W\nDebug.Print Len(x)");
        AssertDebugLog([new Vb6Value((long)width)]);
    }

    [Theory]
    [InlineData("Dim e() As String * 3\nReDim e(1)", "d")]
    [InlineData("Dim e(1) As String * 3", "d")]
    [InlineData("Dim e() As String * 3\nReDim e(1)", "u.d")]
    public async Task Whole_array_is_an_independent_copy(string source, string target)
    {
        await Run($"Type T\nd() As String * 3\nEnd Type\nDim u As T\nDim d() As String * 3\n{source}\ne(0) = \"ab\"\n{target} = e\nDebug.Print LBound({target})\nDebug.Print UBound({target})\nDebug.Print {target}(0)\nDebug.Print {target}(1)\n{target}(0) = \"abcdef\"\nDebug.Print {target}(0)\nDebug.Print e(0)");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(1L), new Vb6Value("ab "), new Vb6Value(new string('\0', 3)), new Vb6Value("abc"), new Vb6Value("ab ")]);
    }

    [Fact]
    public async Task Unallocated_source_erases_target()
    {
        await Run("Dim d() As String * 3, e() As String * 3\nReDim d(2)\nd = e\nOn Error Resume Next\nDebug.Print UBound(d)\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value(9L)]);
    }

    [Theory]
    [InlineData("Dim d() As String * 4", "Dim e(1) As String * 3", "d = e", "Can't assign to array")]
    [InlineData("Dim d() As String * 2", "Dim e(1) As String * 3", "d = e", "Can't assign to array")]
    [InlineData("Dim d() As String", "Dim e(1) As String * 3", "d = e", "Can't assign to array")]
    [InlineData("Dim d() As String * 3", "Dim e(1) As String", "d = e", "Can't assign to array")]
    [InlineData("Dim d(1) As String * 3", "Dim e(1) As String * 3", "d = e", "Can't assign to array")]
    [InlineData("Type T\nd() As String * 4\nEnd Type\nDim u As T", "Dim e(1) As String * 3", "u.d = e", "Can't assign to array")]
    [InlineData("Type T\nd() As String * 3\nEnd Type\nDim u As T", "Dim e(1) As String", "u.d = e", "Can't assign to array")]
    [InlineData("Dim w", "Dim e() As String * 3\nReDim e(1)", "w = e", VariantArrayError)]
    [InlineData("Dim w", "Dim e(1) As String * 3", "w = e", VariantArrayError)]
    [InlineData("", "Dim e(1) As String * 3", "Debug.Print F()\nFunction F() As Variant\nF = e\nEnd Function", VariantArrayError)]
    [InlineData("", "Dim e(1) As String * 3", "P e\nSub P(ByVal x As Variant)\nEnd Sub", VariantArrayError)]
    [InlineData("", "Dim e(1) As String * 3", "P e\nSub P(ByRef x As Variant)\nEnd Sub", VariantArrayError)]
    [InlineData("Const W = True", "", "Dim x As String * W", "Invalid length for fixed-length string")]
    // Measured in #179's review, native and p-code, each its own program.
    [InlineData("Dim s As String", "Dim e() As String * 3\nReDim e(1)", "s = e", "Type mismatch")]
    [InlineData("Dim n As Long", "Dim e() As String * 3\nReDim e(1)", "n = e", "Type mismatch")]
    [InlineData("Type T\nd() As String * 3\nEnd Type\nDim u As T", "Dim w", "ReDim u.d(1)\nw = u.d", VariantArrayError)]
    [InlineData("", "Dim e(1) As String * 3", "Debug.Print UBound(F())\nFunction F() As String()\nF = e\nEnd Function", "Can't assign to array")]
    [InlineData("Const W = False", "", "Dim x As String * W", "Invalid length for fixed-length string")]
    public async Task Measured_compile_errors(string declarations, string source, string statement, string message)
    {
        var run = () => Run($"{declarations}\n{source}\n{statement}");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + message);
    }

    private const string VariantArrayError = "Can't assign or coerce array of fixed-length string or user-defined type to Variant";

    [Theory]
    [InlineData("Split(\"abcdef,x\", \",\" )", true)]
    [InlineData("Split(\"abcdef,x\", \",\" )", false)]
    [InlineData("w", false)]
    [InlineData("w", true)]
    [InlineData("emptyVariant", true)]
    [InlineData("textVariant", true)]
    public async Task Runtime_mismatch_keeps_target(string source, bool allocated)
    {
        await Run($"Dim d() As String * 3, w, emptyVariant, textVariant\ntextVariant = \"text\"\nw = {(allocated ? "Array(\"abcdef\", \"x\")" : "Split(\"abcdef,x\", \",\" )")}\n{(allocated ? "ReDim d(4)\nd(0) = \"zz\"" : "")}\nOn Error Resume Next\nd = {source}\nDebug.Print Err.Number\nErr.Clear\nDebug.Print UBound(d)\n{(allocated ? "Debug.Print d(0)" : "Debug.Print Err.Number")}");
        AssertDebugLog(allocated ? [new Vb6Value(13L), new Vb6Value(4L), new Vb6Value("zz ")] : [new Vb6Value(13L), new Vb6Value(9L)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Redim_As_keeps_width_and_preserved_elements(bool preserve)
    {
        await Run($"Dim b() As String * 4\nReDim b(0)\nb(0) = \"ab\"\nReDim {(preserve ? "Preserve " : "")}b(1) As String * 4\nDebug.Print b(0)\nDebug.Print b(1)\nb(0) = \"abcdefgh\"\nDebug.Print b(0)");
        AssertDebugLog([new Vb6Value(preserve ? "ab  " : new string('\0', 4)), new Vb6Value(new string('\0', 4)), new Vb6Value("abcd")]);
        // The declaration is dynamic in #179's measured setup. ReDim As must retain that
        // storage classification too, which subsequent Erase and Type-field stores consult.
        rootEnv.TryGetVariableLocation("b", out var location).Should().BeTrue();
        context.State[location].Value.Should().BeOfType<VBArray>().Which.IsDynamic.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "String * 4", "String * 6", "Can't change data types of array elements")]
    [InlineData("", "String * 4", "String", "Can't change data types of array elements")]
    [InlineData("", "String", "String * 3", "Can't change data types of array elements")]
    [InlineData("Preserve ", "String * 4", "String * 6", "Can't change data types of array elements")]
    [InlineData("", "Variant", "String * 3", VariantArrayError)]
    public async Task Redim_As_rejects_changed_type(string preserve, string declared, string redim, string message)
    {
        var declaration = declared == "Variant" ? "Dim b As Variant" : $"Dim b() As {declared}";
        var run = () => Run($"{declaration}\nReDim {preserve}b(1) As {redim}");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Option Explicit")]
    public async Task Undeclared_Redim_As_declares_width(string option)
    {
        await Run($"{option}\nSub Main()\nReDim z(1) As String * 5\nz(0) = \"abcdefg\"\nDebug.Print z(0)\nDebug.Print z(1)\nEnd Sub\nMain");
        AssertDebugLog([new Vb6Value("abcde"), new Vb6Value(new string('\0', 5))]);
    }

    [Theory]
    [InlineData("Integer", "fx")]
    [InlineData("Integer", "a(0)")]
    [InlineData("Integer", "u.s")]
    [InlineData("Long", "fx")]
    [InlineData("Long", "a(0)")]
    [InlineData("Long", "u.s")]
    [InlineData("Single", "fx")]
    [InlineData("Single", "a(0)")]
    [InlineData("Single", "u.s")]
    [InlineData("Double", "fx")]
    [InlineData("Double", "a(0)")]
    [InlineData("Double", "u.s")]
    [InlineData("Currency", "fx")]
    [InlineData("Currency", "a(0)")]
    [InlineData("Currency", "u.s")]
    [InlineData("Byte", "fx")]
    [InlineData("Byte", "a(0)")]
    [InlineData("Byte", "u.s")]
    [InlineData("Boolean", "fx")]
    [InlineData("Boolean", "a(0)")]
    [InlineData("Boolean", "u.s")]
    [InlineData("Date", "fx")]
    [InlineData("Date", "a(0)")]
    [InlineData("Date", "u.s")]
    [InlineData("Object", "fx")]
    [InlineData("Object", "a(0)")]
    [InlineData("Object", "u.s")]
    public async Task Fixed_scalar_element_and_field_reject_other_ByRef_types(string type, string argument)
    {
        var run = () => Run($"Type T\ns As String * 5\nEnd Type\nDim u As T, fx As String * 5, a(1) As String * 5\nP {argument}\nSub P(x As {type})\nEnd Sub");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\nByRef argument type mismatch");
    }

    [Theory]
    [InlineData("Dim fx As String * 5", "fx", "String")]
    [InlineData("Dim fx As String * 5", "fx", "Variant")]
    [InlineData("Dim a(1) As String * 3", "a", "String")]
    [InlineData("Dim a(1) As String", "a", "String * 3")]
    // Measured in #179's review, native and p-code.
    [InlineData("Dim l() As Long\nReDim l(1)", "l", "String * 3")]
    [InlineData("Dim v() As Variant\nReDim v(1)", "v", "String * 3")]
    [InlineData("Dim w As Variant\nw = Split(\"a,b\", \",\")", "w", "String * 3")]
    public async Task Array_parameter_requires_matching_array_kind(string declaration, string argument, string type)
    {
        var run = () => Run($"{declaration}\nP {argument}\nSub P(x() As {type})\nEnd Sub");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\nType mismatch: array or user-defined type expected");
    }

    [Theory]
    [InlineData("Dim a(1) As String * 3", false)]
    [InlineData("Dim a() As String * 3\nReDim a(1)", false)]
    [InlineData("Dim a() As String * 3\nReDim a(1)", true)]
    public async Task Matching_array_parameter_shares_elements_and_Redim(string declaration, bool redim)
    {
        await Run($"{declaration}\na(0) = \"ab\"\nP a\nDebug.Print a({(redim ? 3 : 0)})\nDebug.Print UBound(a)\nSub P(x() As String * 3)\nDebug.Print x(0)\n{(redim ? "ReDim x(3)" : "")}\nx({(redim ? 3 : 0)}) = \"abcdef\"\nEnd Sub");
        AssertDebugLog([new Vb6Value("ab "), new Vb6Value("abc"), new Vb6Value(redim ? 3L : 1L)]);
    }

    [Theory]
    [InlineData("\"variantset\"", "variantset", "varia")]
    [InlineData("12", "12", "12   ")]
    [InlineData("1.5", "1.5", "1.5  ")]
    [InlineData("Empty", "", "     ")]
    public async Task Variant_parameter_retains_String_storage(string assigned, string inside, string outside)
    {
        using var locale = VbLocale.Use(System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
        await Run($"Dim fx As String * 5\nfx = \"ab\"\nP fx\nDebug.Print fx\nSub P(x As Variant)\nDebug.Print TypeName(x)\nDebug.Print VarType(x) = vbString\nDebug.Print x\nx = {assigned}\nDebug.Print TypeName(x)\nDebug.Print x\nDebug.Print Len(x)\nEnd Sub");
        AssertDebugLog([new Vb6Value("String"), new Vb6Value(true), new Vb6Value("ab   "), new Vb6Value("String"), new Vb6Value(inside), new Vb6Value((long)inside.Length), new Vb6Value(outside)]);
    }

    [Fact]
    public async Task Variant_parameter_Null_raises_94_and_keeps_String()
    {
        await Run("Dim fx As String * 5\nfx = \"ab\"\nP fx\nDebug.Print fx\nSub P(x As Variant)\nOn Error Resume Next\nx = Null\nDebug.Print Err.Number\nDebug.Print TypeName(x)\nDebug.Print x\nEnd Sub");
        AssertDebugLog([new Vb6Value(94L), new Vb6Value("String"), new Vb6Value("ab   "), new Vb6Value("ab   ")]);
    }

    [Fact]
    public async Task Record_copy_owns_static_array()
    {
        await Run("Type T\na(1) As String * 3\nEnd Type\nDim u As T, u2 As T\nu.a(0) = \"ab\"\nu2 = u\nu2.a(0) = \"zzzz\"\nDebug.Print u2.a(0)\nDebug.Print u.a(0)");
        AssertDebugLog([new Vb6Value("zzz"), new Vb6Value("ab ")]);
    }

    [Fact]
    public async Task Local_Redim_and_parenthesized_ByRef_keep_caller_width()
    {
        await Run("Sub Main()\nDim z() As String * 5\nReDim z(1)\nz(0) = \"abcdefg\"\nDebug.Print z(0)\nDim fx As String * 5\nfx = \"12\"\nP (fx)\nDebug.Print fx\nEnd Sub\nSub P(n As Integer)\nn = 7\nEnd Sub\nMain");
        AssertDebugLog([new Vb6Value("abcde"), new Vb6Value("12   ")]);
    }

    [Fact]
    public async Task String_parameter_has_ordinary_width_until_copy_out()
    {
        await Run("Dim fx As String * 5\nP fx\nDebug.Print fx\nSub P(s As String)\ns = \"xyzxyzxyz\"\nDebug.Print Len(s)\nEnd Sub");
        AssertDebugLog([new Vb6Value(9L), new Vb6Value("xyzxy")]);
    }
    [Theory]
    [InlineData("Dim a(1) As String * 4\nP a\nSub P(x() As String * 3)\nEnd Sub", "Passing an array of another fixed-length String width is not implemented (#179)")]
    [InlineData("Dim fx As String * 5\nP fx\nSub P(x() As Long)\nEnd Sub", "ByRef fixed-length String to this parameter type is not implemented (#179)")]
    [InlineData("Dim a(1) As String * 3\nP a\nSub P(x() As Long)\nEnd Sub", "Fixed-length String array to this parameter type is not implemented (#179)")]
    [InlineData("Type T\nn As Long\nEnd Type\nDim fx As String * 5\nP fx\nSub P(x As T)\nEnd Sub", "ByRef fixed-length String to this parameter type is not implemented (#179)")]
    public async Task Unmeasured_parameter_edges_stay_refused(string program, string message)
    {
        var run = () => Run(program);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Be(message);
    }

}
