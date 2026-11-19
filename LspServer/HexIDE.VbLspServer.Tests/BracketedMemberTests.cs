using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class BracketedMemberTests
{
    [Theory]
    [InlineData("x = SSTab1.[_Tab]")]
    [InlineData("SSTab1.[_Tab] = 1")]
    [InlineData("With SSTab1\nx = .[_Tab]\nEnd With")]
    [InlineData("x = record![_Field]")]
    [InlineData("Set x = items.[_NewEnum]")]
    public void BracketedMemberHasNoDiagnostics(string statement)
    {
        VbDiagnosticsProvider.GetDiagnostics($"Sub Main()\n{statement}\nEnd Sub\n").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Dim [_x] As Long")]
    [InlineData("Dim x As Project.[_x]")]
    public void UnderscoreLedBracketedDeclarationStillHasDiagnostics(string declaration)
    {
        VbDiagnosticsProvider.GetDiagnostics(declaration + "\n").Should().NotBeEmpty();
    }
}
