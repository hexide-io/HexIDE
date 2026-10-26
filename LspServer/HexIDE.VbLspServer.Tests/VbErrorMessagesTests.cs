// SPDX-License-Identifier: MIT
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class VbErrorMessagesTests
{
    public static TheoryData<string, string, string> WhitespaceTokens
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var token in new[] { "\n", "\r\n", "\r", " ", "\t", " \r\n\t" })
            {
                data.Add("mismatched input '\\n' expecting IDENTIFIER", token,
                    "Syntax error: unexpected 'end of line'");
                data.Add("extraneous input '\\n' expecting IDENTIFIER", token,
                    "Syntax error: unexpected 'end of line'");
                data.Add("no viable alternative at input '\\n'", token,
                    "Syntax error near 'end of line'");
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(WhitespaceTokens))]
    public void WhitespaceToken_IsNamedEndOfLine(string message, string token, string expected)
    {
        VbErrorMessages.Prettify(message, token).Should().Be(expected);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void UnfinishedExpression_NamesEndOfLine(string newline)
    {
        var code = string.Join(newline, "Private Sub Form_Load()", "    Dim a As Integer",
            "    a = (1 +", "End Sub");

        VbDiagnosticsProvider.GetDiagnostics(code).Should().Contain(d =>
            d.Message == "Syntax error: unexpected 'end of line'" &&
            d.Range.Start.Line == 2 && d.Range.Start.Character == 12);
    }
}
