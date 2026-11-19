using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured with vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4933.
public class VbaQualifiedLenTests : BaseVBTestFixture
{
    private const string Declarations = "Private Type T1\nx As Integer\ns As String * 3\nEnd Type\nPrivate u1 As T1\nPrivate arr(2) As T1\n";
    private const string CompileError = "Only user-defined types defined in public object modules can be coerced to or from a variant or passed to late-bound functions";

    [Theory]
    [InlineData("VBA.Len(u1)")]
    [InlineData("VBA.Strings.Len(u1)")]
    [InlineData("VBA.LenB(u1)")]
    [InlineData("VBA.Len(arr(1))")]
    public async Task Qualified_length_of_a_module_type_is_a_compile_error(string expression)
    {
        Func<Task> run = () => Run(Declarations + $"Debug.Print {expression}\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + CompileError);
    }

    [Fact]
    public async Task Qualified_length_of_an_integer_only_type_is_a_compile_error()
    {
        Func<Task> run = () => Run("Private Type T3\nx As Integer\nEnd Type\nPrivate u3 As T3\nDebug.Print VBA.Len(u3)\n");
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + CompileError);
    }

    [Fact]
    public async Task Qualified_length_of_a_private_class_type_is_a_compile_error()
    {
        Func<Task> run = () => RunClasses("Dim c As New C\nDebug.Print c.M\n",
            ("C", "Private Type T\nx As Integer\nEnd Type\nPrivate u As T\nPublic Function M() As Long\nM = VBA.Len(u)\nEnd Function\n"));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.ReplaceLineEndings("\n").Should().Be("Compile error:\n\n" + CompileError);
    }

    [Theory]
    [InlineData("u1.x", 1L)]
    [InlineData("u1.s", 3L)]
    public async Task Qualified_length_of_a_field_still_returns_a_Long(string field, long length)
    {
        await Run(Declarations + $"Debug.Print VBA.Len({field})\nDebug.Print TypeName(VBA.Len({field}))\n");
        AssertDebugLog([new Vb6Value(length), "Long"]);
    }

    [Theory]
    [InlineData("u1")]
    [InlineData("arr(1)")]
    public async Task Unqualified_Len_still_sizes_the_type(string expression)
    {
        await Run(Declarations + $"Debug.Print Len({expression})\n");
        debug.Should().HaveCount(1);
        Convert.ToDouble(debug[0].Value).Should().Be(5);
    }

    [Fact]
    public async Task Unqualified_LenB_keeps_its_existing_refusal()
    {
        Func<Task> run = () => Run(Declarations + "Debug.Print LenB(u1)\n");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*LenB of a user-defined type*#1510*");
    }
}
