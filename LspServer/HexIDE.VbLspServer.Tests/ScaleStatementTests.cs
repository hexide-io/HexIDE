using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// #990, docs/vb6-fidelity-oracle/the-coordinate-system-scalemode-scale-scalex-and-scaley.md.
public class ScaleStatementTests
{
    [Theory]
    [InlineData("Scale (0, 100)-(10, 0)")]
    [InlineData("Me.Scale (0, 100)-(10, 0)")]
    [InlineData("Form1.Scale (0, 0)-(1, 1)")]
    [InlineData("Picture1.Scale (0, 100)-(10, 0)")]
    [InlineData("Scale")]
    [InlineData("Me.Scale")]
    [InlineData("ScaleMode = 3")]
    public void Scale_and_its_properties_parse(string statement) =>
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n" + statement + "\nEnd Sub\n").Should().BeEmpty();

    [Theory]
    [InlineData("Dim Scale As Integer")]
    [InlineData("Scale:")]
    [InlineData("End Sub\nSub Scale()")]
    public void Scale_is_reserved(string statement) =>
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n" + statement + "\nEnd Sub\n").Should().NotBeEmpty();
}
