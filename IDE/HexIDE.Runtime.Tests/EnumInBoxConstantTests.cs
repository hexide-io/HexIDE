using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured with vb6.exe SP6 /make, native and p-code, 2026-11-18: #4594's body and supervisor comment.
public class EnumInBoxConstantTests : BaseVBTestFixture
{
    private static string Declaration(string expression) => $"Enum E\nM = {expression}\nEnd Enum\n";

    [Theory]
    [InlineData("VBA.vbBinaryCompare", 0L)]
    [InlineData("vbTextCompare", 1L)]
    [InlineData("vbObjectError Or 127", -2147221377L)]
    [InlineData("vbRed", 255L)]
    [InlineData("VBRUN.vbRed", 255L)]
    [InlineData("VBA.VbCompareMethod.vbDatabaseCompare", 2L)]
    [InlineData("VbCompareMethod.vbDatabaseCompare", 2L)]
    public async Task Measured_values(string expression, long expected)
    {
        await Run(Declaration(expression) + "Debug.Print E.M\nDebug.Print TypeName(E.M)\n");
        AssertDebugLog([new Vb6Value(expected), new Vb6Value("Long")]);
    }

    [Fact]
    public async Task Library_must_declare_member()
    {
        var act = () => Run(Declaration("VBA.vbRed"));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message
            .ReplaceLineEndings("\n").Should().Be("Compile error:\n\nMethod or data member not found");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_Const_outranks_library(bool otherModule)
    {
        await RunModules((otherModule ? "" : "Const vbRed As Long = 3\n") + Declaration("vbRed") + "Debug.Print E.M\n",
            ("Module2", otherModule ? "Public Const vbRed As Long = 3\n" : ""));
        AssertDebugLog([new Vb6Value(3L)]);
    }

    [Theory]
    [InlineData("Dim vbRed As Long\n", "", "", "vbRed")]
    [InlineData("", "", "Public vbRed As Long\n", "vbRed + 0")]
    [InlineData("", "Const vbRed As Long = 3\n", "", "vbRed")]
    public async Task Existing_constant_expression_refusals(string before, string after, string other, string expression)
    {
        var act = () => RunModules(before + Declaration(expression) + after, ("Module2", other));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Constant expression required");
    }

    [Theory]
    [InlineData("VBA.vbBinaryCompare()")]
    [InlineData("VBA.VbCompareMethod.vbDatabaseCompare.Other")]
    public async Task Other_expression_forms_keep_the_existing_refusal(string expression)
    {
        var act = () => Run(Declaration(expression));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Constant expression required");
    }

    // SP6 native/p-code measurements: https://github.com/hexide-io/HexIDE/issues/4745
    [Theory]
    [InlineData("vbCrLf", "Type mismatch")]
    [InlineData("VBA.vbCrLf", "Type mismatch")]
    [InlineData("vbNullString", "Type mismatch")]
    [InlineData("vbTab", "Type mismatch")]
    [InlineData("VbCompareMethod.vbRed", "Method or data member not found")]
    [InlineData("VBA.VbCompareMethod.vbRed", "Method or data member not found")]
    [InlineData("VBA.ColorConstants.vbRed", "Method or data member not found")]
    [InlineData("VBRUN.VbCompareMethod.vbBinaryCompare", "Method or data member not found")]
    [InlineData("VBA.NoSuch.vbRed", "Method or data member not found")]
    [InlineData("VBA.vbRed.Other", "Method or data member not found")]
    [InlineData("VBA.vbBinaryCompare.Other", "Invalid qualifier")]
    public async Task Enum_library_compile_errors(string expression, string expected)
    {
        var act = () => Run(Declaration(expression));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message
            .ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + expected);
    }

    [Theory]
    [InlineData("VbCompareMethod.vbRed", "Method or data member not found")]
    [InlineData("VBA.VbCompareMethod.vbRed", "Method or data member not found")]
    [InlineData("VBA.ColorConstants.vbRed", "Method or data member not found")]
    [InlineData("VBRUN.VbCompareMethod.vbBinaryCompare", "Method or data member not found")]
    [InlineData("VBA.NoSuch.vbRed", "Method or data member not found")]
    [InlineData("VBA.vbRed.Other", "Method or data member not found")]
    [InlineData("VBA.vbBinaryCompare.Other", "Invalid qualifier")]
    public async Task Const_library_compile_errors(string expression, string expected)
    {
        var act = () => Run($"Const M = {expression}\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message
            .ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + expected);
    }

    // VB6 SP6 native/p-code: #4614's latest supervisor comment and
    // docs/vb6-fidelity-oracle/what-an-enum-members-value-and-a-types-bounds-may-name.md.
    [Theory]
    [InlineData("K + 1", "Const K As Long = 5\n", "", 6L)]
    [InlineData("K + 1", "Const K As Long = 3\n", "", 4L)]
    [InlineData("Module2.PC + 1", "", "Public Const PC As Long = 40\n", 41L)]
    [InlineData("PC * 2", "", "Public Const PC As Long = 40\n", 80L)]
    public async Task Project_constant_values(string expression, string before, string other, long expected)
    {
        await RunModules(before + Declaration(expression) + "Debug.Print E.M\nDebug.Print TypeName(M)\n",
            ("Module2", other));
        AssertDebugLog([new Vb6Value(expected), new Vb6Value("Long")]);
    }

    private const string Owner = "Const K As Long = 5\nPublic Enum E\nM = K + 1\nN\nEnd Enum\n";

    [Fact]
    public async Task Following_member()
    {
        await RunModules(Owner + "Debug.Print N\n");
        AssertDebugLog([new Vb6Value(7L)]);
    }

    [Fact]
    public async Task Exported_member_in_const()
    {
        await RunModules("Const C As Long = M + 1\nDebug.Print C\n", ("Module2", Owner));
        AssertDebugLog([new Vb6Value(7L)]);
    }

    // Composition measured in #4742, quoted in #4614.
    [Fact]
    public async Task Composition()
    {
        await RunModules("Const C As Long = M + 1\nConst D As Long = Module2.N + 10\nDim a(M) As Long\n"
            + "Debug.Print M, N, E.M, Module2.M, E.N\nDebug.Print C, D, UBound(a), TypeName(M)\n",
            ("Module2", Owner));
        AssertDebugLog([new Vb6Value(6L), new Vb6Value(7L), new Vb6Value(6L), new Vb6Value(6L),
            new Vb6Value(7L), new Vb6Value(7L), new Vb6Value(17L), new Vb6Value(6L), new Vb6Value("Long")]);
    }

    [Theory]
    [InlineData("Public Const C As Long = M + 1", "C", 7L)]
    [InlineData("Public Const D As Long = ModuleB.N + 10", "D", 17L)]
    [InlineData("Public a(M) As Long", "UBound(a)", 6L)]
    public async Task Reader_declarations_before_owner(string declaration, string expression, long expected)
    {
        await RunModules("Debug.Print " + expression + "\n", ("ModuleA", declaration + "\n"), ("ModuleB", Owner));
        AssertDebugLog([new Vb6Value(expected)]);
    }

    [Fact]
    public async Task Own_constant_below_enum_is_refused()
    {
        var act = () => RunModules(Declaration("K") + "Const K As Long = 5\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Constant expression required");
    }

    [Theory]
    [InlineData("vbCrLf")]
    [InlineData("VBA.vbCrLf")]
    public async Task Const_accepts_String_constant(string expression)
    {
        await Run($"Const M = {expression}\nDebug.Print M\nDebug.Print TypeName(M)\n");
        AssertDebugLog([new Vb6Value("\r\n"), new Vb6Value("String")]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Library_container_member_control(bool inEnum)
    {
        await Run((inEnum ? Declaration("VBRUN.ColorConstants.vbRed") : "Const M = VBRUN.ColorConstants.vbRed\n")
            + "Debug.Print M\nDebug.Print TypeName(M)\n");
        AssertDebugLog([new Vb6Value(255L), new Vb6Value("Long")]);
    }

    // These are HexIDE's explicit refusals, not additional VB6 measurements.
    // VB6 SP6 native/p-code measurements, 2026-11-18: #4762's supervisor comment (oracle PR #4818).
    [Theory]
    [InlineData("VBA.vbBinaryCompare", "VBA", "", "Name conflicts with existing module, project, or object library")]
    [InlineData("VBRUN.vbRed", "Module2", "Public Enum VBRUN\nOther = 3\nEnd Enum\n", "Name conflicts with existing module, project, or object library")]
    [InlineData("VbCompareMethod.vbDatabaseCompare", "VbCompareMethod", "", "Method or data member not found")]
    public async Task Project_qualifier_hides_library(string expression, string module, string source, string error)
    {
        var act = () => RunModules(Declaration(expression), (module, source));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message
            .ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + error);
    }

    [Theory]
    [InlineData(true, "Sub vbRed()\nEnd Sub\n")]
    [InlineData(false, "Public Sub vbRed()\nEnd Sub\n")]
    [InlineData(false, "Public Declare Sub vbRed Lib \"x\" ()\n")]
    public async Task Project_Sub_hides_library(bool own, string source)
    {
        var act = () => RunModules(Declaration("vbRed") + (own ? source : ""), ("Module2", own ? "" : source));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message
            .ReplaceLineEndings("\n").Should().Be("Compile error:\n\nExpected Function or variable");
    }

    [Fact]
    public async Task Public_Enum_member_hides_library()
    {
        await RunModules(Declaration("vbRed") + "Debug.Print M\nDebug.Print TypeName(M)\n",
            ("Module2", "Public Enum O\nvbRed = 3\nEnd Enum\n"));
        AssertDebugLog([new Vb6Value(3L), new Vb6Value("Long")]);
    }

    [Theory]
    [InlineData("VbCompareMethod.vbDatabaseCompare", "VbCompareMethod", "Public Const vbDatabaseCompare As Long = 3\n")]
    // A module named after a library, not a library Enum: VB6's answer is unmeasured.
    [InlineData("VBRUN.vbRed", "VBRUN", "")]
    public async Task Unmeasured_qualifier_member_stays_refused(string expression, string module, string source)
    {
        var act = () => RunModules(Declaration(expression), (module, source));
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#4914");
    }

    // Explicit HexIDE refusals; these are not VB6 measurements.
    [Theory]
    [InlineData(true, "Declare Sub vbRed Lib \"x\" ()\n")]
    [InlineData(true, "Public Enum O\nvbRed = 3\nEnd Enum\n")]
    [InlineData(false, "Public Function vbRed() As Long\nEnd Function\n")]
    [InlineData(false, "Private Enum O\nvbRed = 3\nEnd Enum\n")]
    public async Task Unmeasured_project_names_stay_refused(bool own, string source)
    {
        var act = () => RunModules(Declaration("vbRed") + (own ? source : ""), ("Module2", own ? "" : source));
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#4914").And.Contain("M");
    }
}
