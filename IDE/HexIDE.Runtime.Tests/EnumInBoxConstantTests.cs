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
    public async Task Project_Const_requires_its_value(bool otherModule)
    {
        var act = () => RunModules((otherModule ? "" : "Const vbRed As Long = 3\n") + Declaration("vbRed"),
            ("Module2", otherModule ? "Public Const vbRed As Long = 3\n" : ""));
        var error = (await act.Should().ThrowAsync<NotImplementedException>()).Which;
        error.Message.Should().Contain("M").And.Contain("#4614");
    }

    [Theory]
    [InlineData("Dim vbRed As Long\n", "", "", "vbRed")]
    [InlineData("", "", "Public vbRed As Long\n", "vbRed + 0")]
    [InlineData("", "Const vbRed As Long = 3\n", "", "vbRed")]
    [InlineData("Const K As Long = 3\n", "", "", "K + 1")]
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

    [Theory]
    [InlineData("VBA.vbBinaryCompare", "VBA", "")]
    [InlineData("VBA.vbRed", "VBA", "")]
    [InlineData("VBRUN.vbRed", "Module2", "Public Enum VBRUN\nOther = 3\nEnd Enum\n")]
    [InlineData("VbCompareMethod.vbDatabaseCompare", "VbCompareMethod", "")]
    public async Task Project_qualifier_requires_project_resolution(string expression, string module, string source)
    {
        var act = () => RunModules(Declaration(expression), (module, source));
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#4614").And.Contain("M");
    }

    [Theory]
    [InlineData("Sub vbRed()\nEnd Sub\n")]
    [InlineData("Declare Sub vbRed Lib \"unused\" ()\n")]
    [InlineData("Public Enum OtherEnum\nvbRed = 3\nEnd Enum\n")]
    public async Task Other_project_names_are_not_library_values(string source)
    {
        var act = () => RunModules(Declaration("vbRed"), ("Module2", source));
        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#4614").And.Contain("M");
    }
}
