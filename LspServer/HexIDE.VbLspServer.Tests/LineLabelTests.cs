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

    // A number or label on the line that closes a block, or opens its next part, as vb6.exe compiles each (measured,
    // #2529). A number had no place in the grammar there, so the line was a syntax error.
    [Theory]
    [InlineData("Sub S()\r\n10 For i = 1 To 2\r\n20 x = i\r\n30 Next\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 For i = 1 To 2\r\n20 x = i\r\n30 Next i\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 For Each v In c\r\n20 x = v\r\n30 Next\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 If x = 0 Then\r\n20 x = 1\r\n30 End If\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 If x = 1 Then\r\n20 x = 2\r\n30 Else\r\n40 x = 3\r\n50 End If\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 If x = 1 Then\r\n20 x = 2\r\n30 ElseIf x = 0 Then\r\n40 x = 3\r\n50 End If\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 Do\r\n20 x = x + 1\r\n30 Loop While x < 3\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 Do While x < 3\r\n20 x = x + 1\r\n30 Loop\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 While x < 3\r\n20 x = x + 1\r\n30 Wend\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 Select Case x\r\n    Case 0\r\n30 x = 1\r\n40 Case Else\r\n50 x = 2\r\n60 End Select\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 With c\r\n20 .Add 1\r\n30 End With\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n10 x = 1\r\n90 End Sub\r\n")]
    [InlineData("Function F()\r\n10 F = 1\r\n90 End Function\r\n")]
    [InlineData("Property Get P()\r\n10 P = 1\r\n90 End Property\r\n")]
    [InlineData("Sub S()\r\n10 For i = 1 To 2\r\n30 Next\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    For i = 1 To 3\r\n        If i = 2 Then GoTo Skip\r\n        x = i\r\nSkip: Next\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    If x = 0 Then\r\n        GoTo Skip\r\nSkip: Else\r\n        x = 1\r\n    End If\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    For i = 1 To 3\r\n30 Skip: Next\r\nEnd Sub\r\n")]
    public void AHeadOnAClosingOrMiddleLineProducesNoDiagnostics(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Fact]
    public void ALineNumberBeforeTheFirstCaseIsStillAnError()
    {
        // vb6.exe refuses it: "Statements and labels invalid between Select Case and first Case" (measured, #2529).
        VbDiagnosticsProvider.GetDiagnostics("Sub S()\r\n10 Select Case x\r\n20 Case 0\r\n30 x = 1\r\n40 End Select\r\nEnd Sub\r\n")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void ALabelMayPrecedeABlockOpener()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub S()\r\nChk: If True Then\r\n    Debug.Print 1\r\nEnd If\r\nEnd Sub\r\n")
            .Should().BeEmpty();
    }
}
