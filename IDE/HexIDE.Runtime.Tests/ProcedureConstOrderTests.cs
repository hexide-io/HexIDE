using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ProcedureConstOrderTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("", "Const A = B + 1\nConst B = 2", "Constant expression required")]
    [InlineData("Option Explicit\n", "Const A = B + 1\nConst B = 2", "Variable not defined")]
    [InlineData("", "Dim a(N) As Long\nConst N = 3", "Constant expression required")]
    [InlineData("Option Explicit\n", "Dim a(N) As Long\nConst N = 3", "Variable not defined")]
    [InlineData("Option Explicit\nConst B = 10\n", "Const A = B + 1\nConst B = 2", "Duplicate declaration in current scope")]
    public async Task A_declaration_cannot_read_a_later_local_Const(string prefix, string body, string error)
    {
        var run = () => Run($"{prefix}Go\nSub Go()\n{body}\nEnd Sub\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage($"*{error}*");
    }

    [Fact]
    public async Task A_local_Const_can_shadow_a_module_Const_without_an_earlier_use()
    {
        await Run("Option Explicit\nConst B = 10\nGo\nSub Go()\nConst B = 2\nDebug.Print B\nEnd Sub\n");

        AssertDebugLog([new Vb6Value(2)]);
    }
}
