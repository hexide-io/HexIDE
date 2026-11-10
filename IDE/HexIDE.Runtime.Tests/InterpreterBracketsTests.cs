using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class InterpreterBracketsTests
{
    [Theory]
    [InlineData("(())()", new[] { 0, 1, 1, 0, 0, 0 })]
    [InlineData(")(())(", new[] { -1, 0, 1, 1, 0, -1 })]
    [InlineData("( _\r\n())", new[] { 0, 1, 1, 0 })]
    [InlineData("\"(\" & \"a\"\"(\" ' (\r\n()", new[] { 0, 0 })]
    [InlineData("Rem (\r\n' )\r\n()", new[] { 0, 0 })]
    public void Scan_UsesCodeTokensAndPairs(string source, int[] depths)
    {
        var marks = new InterpreterBrackets().Brackets(source);
        marks.Select(mark => mark.Depth).Should().Equal(depths);
        marks.Select(mark => source[mark.Offset]).Should().OnlyContain(c => c == '(' || c == ')');
    }
}
