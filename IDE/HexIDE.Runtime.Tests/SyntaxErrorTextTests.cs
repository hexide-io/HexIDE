using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #3210: a failed prediction that began at the top of the module quotes the module's text up to the error. The
// message keeps only the line the error is on; corpus case 05122 quoted 6,638 characters.
public class SyntaxErrorTextTests : BaseVBTestFixture
{
    // Corpus case 03842. The quote reaches back to the top only when the module starts with its attribute line.
    private const string Module = "Attribute VB_Name = \"Module1\"\n"
        + "Sub Foo(Optional a, Optional b, Optional c)\nEnd Sub\nSub Main()\n    Foo ,\nEnd Sub\n";

    [Fact]
    public async Task A_syntax_error_names_its_own_line_not_the_module_before_it()
    {
        var run = () => Run(Module);
        var error = (await run.Should().ThrowAsync<VBCompileErrorException>()).Which;
        error.Line.Should().Be(5);
        error.Message.Should().NotContain("Optional");
        error.Message.Should().NotContain("Attribute");
    }

    [Fact]
    public void The_syntax_check_names_its_own_line_too()
    {
        var check = () => new SyntaxChecker().Run(Module);
        var error = check.Should().Throw<VBCompileErrorException>().Which;
        error.Line.Should().Be(5);
        error.Message.Should().NotContain("Optional");
    }

    [Theory]
    [InlineData(@"no viable alternative at input 'Sub Main()\r\n    Foo ,\r\n'", "no viable alternative at input 'Foo ,'")]
    [InlineData(@"no viable alternative at input 'a\nb'", "no viable alternative at input 'b'")]
    [InlineData("no viable alternative at input 'Foo ,'", "no viable alternative at input 'Foo ,'")]
    [InlineData("mismatched input 'x' expecting END_SUB", "mismatched input 'x' expecting END_SUB")]
    public void Only_a_multi_line_quote_is_cut(string message, string expected)
    {
        SyntaxErrorText.Of(message).Should().Be(expected);
    }
}
