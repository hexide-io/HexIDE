using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// #1615: measured in the oracle's "A numeric literal ending in its point". vb6.exe compiles a literal that ends in its point,
// `5.` or `5.D1`, as the literal it would be with a digit after it. The editor marked each. Mirrored on the interpreter's lexer.
public class TrailingPointLiteralGrammarTests
{
    [Theory]
    [InlineData("5.")]
    [InlineData("-5.")]
    [InlineData("0.")]
    [InlineData("5.#")]
    [InlineData("5.!")]
    [InlineData("5.@")]
    [InlineData("5.E1")]
    [InlineData("5.D1")]
    [InlineData("5.E-1")]
    [InlineData("2.*3.")]
    public void A_literal_ending_in_its_point_parses(string literal)
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n    x = " + literal + "\nEnd Sub\n").Should().BeEmpty();
    }

    [Fact]
    public void One_parses_as_an_argument_an_index_and_a_constant()
    {
        VbDiagnosticsProvider.GetDiagnostics(
            "Const K = 5.\nSub Main()\n    Dim a(3) As Integer\n    a(2.) = 7\n    x = Two(5., 2.)\nEnd Sub\n").Should().BeEmpty();
    }

    [Theory]
    [InlineData("5..5")]
    [InlineData("5.%")]
    [InlineData("2.5%")]
    public void Two_points_or_an_Integer_suffix_is_still_an_error(string literal)
    {
        // vb6.exe's "Syntax error", native and p-code.
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n    x = " + literal + "\nEnd Sub\n").Should().NotBeEmpty();
    }
}
