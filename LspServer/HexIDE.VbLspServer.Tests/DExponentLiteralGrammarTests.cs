using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// #1611: measured in the oracle's "A D exponent in a source literal". vb6.exe compiles a D exponent wherever it compiles an
// E one, in either case. A decimal literal already lexed one here; a whole number did not, so the editor marked `x = 1D3`.
// Mirrored on the interpreter's lexer.
public class DExponentLiteralGrammarTests
{
    [Theory]
    [InlineData("1D3")]
    [InlineData("1d3")]
    [InlineData("1D3!")]
    [InlineData("1D3#")]
    [InlineData("1D3@")]
    [InlineData("1D300")]
    [InlineData("2.5D2")]
    [InlineData(".5D1")]
    [InlineData("1E3")]
    public void A_literal_with_a_D_or_E_exponent_parses(string literal)
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n    x = " + literal + "\nEnd Sub\n").Should().BeEmpty();
    }

    [Fact]
    public void A_Const_and_a_directive_take_one()
    {
        VbDiagnosticsProvider.GetDiagnostics("Const K = 1D3\n#Const C = 1D3\nSub Main()\n#If C = 1000 Then\n    x = K\n#End If\nEnd Sub\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void A_name_like_D2_is_still_a_name()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n    Dim D2 As Integer\n    D2 = 7\n    x = 1 + D2\nEnd Sub\n").Should().BeEmpty();
    }

    [Theory]
    [InlineData("1D")]
    [InlineData("1 D3")]
    public void A_D_with_no_exponent_is_still_an_error(string literal)
    {
        // vb6.exe's "Syntax error", native and p-code.
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n    x = " + literal + "\nEnd Sub\n").Should().NotBeEmpty();
    }
}
