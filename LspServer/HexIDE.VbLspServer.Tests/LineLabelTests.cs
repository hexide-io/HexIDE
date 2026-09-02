// SPDX-License-Identifier: MIT
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// Line labels. The interpreter half of this fix is about whether a label becomes a jump target; the LSP
/// half is about whether the editor underlines it.
///
/// <para>
/// Those are different failures with different symptoms, and only the second one is visible while typing.
/// A label form the LSP grammar refuses gets a red squiggle on a line that runs perfectly, which is the
/// worse of the two for someone learning what VB6 allows — they will believe the editor.
/// </para>
/// </summary>
public class LineLabelTests
{
    [Theory]
    [InlineData("Sub S()\r\n    GoTo Skip\r\nSkip: Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Skip\r\nSkip:\r\n    Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Skip\r\nSkip:Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Later\r\nLater : Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Later\r\nLater\t: Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Skip\r\nSkip:: Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Skip\r\n            Skip: Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo my_label\r\nmy_label: Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Skip\r\nSkip _\r\n: Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Skip\r\nSkip: _\r\n    Debug.Print 1\r\nEnd Sub\r\n")]
    public void ALabelAtALineHeadProducesNoDiagnostics(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sub S()\r\n    GoTo Error\r\nError: Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo Name\r\nName: Debug.Print 1\r\nEnd Sub\r\n")]
    public void ALabelMayBeNamedAfterAKeyword(string code)
    {
        // These were refused outright before the label became a line head — the statement rules for
        // `Error` and `Name` won, and the module would not open.
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Fact]
    public void ALineNumberAndALabelMayShareAHead()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub S()\r\n    GoTo Skip\r\n10 Skip: Debug.Print 1\r\nEnd Sub\r\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void ALabelMayPrecedeABlockOpener()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub S()\r\nChk: If True Then\r\n    Debug.Print 1\r\nEnd If\r\nEnd Sub\r\n")
            .Should().BeEmpty();
    }
}
