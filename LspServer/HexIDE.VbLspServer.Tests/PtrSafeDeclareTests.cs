using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class PtrSafeDeclareTests
{
    [Fact]
    public void PtrSafeRemainsAnIdentifierInABareProcedureCall()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub PtrSafe()\nEnd Sub\nSub Main()\nPtrSafe\nEnd Sub\n")
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long")]
    [InlineData("Public Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal milliseconds As Long)")]
    [InlineData("declare ptrsafe function GetTickCount lib \"kernel32\" () as long")]
    public void APtrSafeDeclarationProducesNoDiagnostics(string declaration)
    {
        VbDiagnosticsProvider.GetDiagnostics(declaration + "\nSub Main()\nEnd Sub\n").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long")]
    [InlineData("Private Declare Function PtrSafe Lib \"kernel32\" () As Long")]
    [InlineData("Private Declare Sub PtrSafe Lib \"kernel32\" ()")]
    public void AnExistingDeclarationStillParses(string declaration)
    {
        VbDiagnosticsProvider.GetDiagnostics(declaration + "\nSub Main()\nEnd Sub\n").Should().BeEmpty();
    }
}
