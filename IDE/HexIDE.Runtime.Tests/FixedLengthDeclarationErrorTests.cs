using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe native/p-code compile errors recorded in #179's supervisor comment (oracle PR #4019).
public class FixedLengthDeclarationErrorTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Sub P(s As String * 5)\nEnd Sub", "Expected array")]
    [InlineData("Sub P(ByVal s As String * 5)\nEnd Sub", "Expected array")]
    [InlineData("Function F() As String * 5\nEnd Function", "Expected: end of statement")]
    [InlineData("Property Get Q() As String * 5\nEnd Property", "Expected: end of statement")]
    public void Signatures_report_measured_compile_error(string source, string expected)
    {
        var load = () => new BasicInterpreter(new MockStdLib(debug), context, rootEnv, source + "\n");
        load.Should().Throw<VBCompileErrorException>().Which.Message.Should().Contain(expected);
    }
}
