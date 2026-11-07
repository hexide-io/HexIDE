using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class SeparatorIssue223Tests
{
    [Theory]
    [InlineData(": Debug.Print \"A\"\n")]
    [InlineData(":  :  : Debug.Print \"A\"\n")]
    [InlineData(": Debug.Print \"A\"\nDebug.Print \"B\"\n")]
    [InlineData("If True Then\nDebug.Print \"T\"\nFin: End If\nDebug.Print \"AFTER\"\n")]
    [InlineData("Dim x As Boolean\nx = True\nIf x Then\nDebug.Print \"A\"\nSkip: End If\n")]
    [InlineData("If True Then:\nDebug.Print \"A\"\n")]
    public void MeasuredIssueCases(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n" + code + "End Sub\n").Should().BeEmpty();
    }
}
