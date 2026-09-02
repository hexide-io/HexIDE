// SPDX-License-Identifier: MIT
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// <c>Rem</c> takes no separator before its text — <c>Rem</c>, <c>Rem:</c>, <c>Rem=1</c> and <c>Rem'x</c>
/// are all comments, measured against vb6.exe against a documented rule that says otherwise.
///
/// <para>
/// The LSP half needs its own evidence rather than inheriting the interpreter's, because the corpus gate
/// only ever runs against the interpreter's grammar. A user meets the drift as an editor underlining code
/// that runs perfectly well — and for this change the reverse is worse: a comment rule that starts too
/// eagerly would grey out a live assignment in the editor and show no error at all.
/// </para>
/// </summary>
public class RemFormTests
{
    [Theory]
    [InlineData("Sub S()\r\n    Rem\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Rem:\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Rem=1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Rem'quoted\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Rem\"text\"\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Rem\ttab separated\r\nEnd Sub\r\n")]
    public void RemNeedsNoSeparator(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sub S()\r\n    Dim RemX As Long\r\n    RemX = 5\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Dim Rem1 As Long\r\n    Rem1 = 7\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Dim Remainder As Long\r\n    Remainder = 3\r\nEnd Sub\r\n")]
    public void AnIdentifierMayBeginWithRem(string code)
    {
        // The hazard the REMTAIL guard exists for. A rule that starts the comment at `Rem` regardless of
        // what follows would grey these assignments out and report nothing wrong.
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sub S()\r\n    GoTo 10\r\n10 Rem arrived\r\n    Debug.Print 1\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    GoTo 20\r\n20\r\n    Debug.Print 1\r\nEnd Sub\r\n")]
    public void ALineNumberMayStandAloneOnItsLine(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sub S()\r\n    Dim x As Long\r\n    If True Then x = 1 Else Rem nothing\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Dim x As Long\r\n    If True Then x = 1 Else\r\nEnd Sub\r\n")]
    public void ASingleLineIfMayHaveAnEmptyElse(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Fact]
    public void AColonMayFollowThenWithNoSpaceAtAll()
    {
        // `Then:` with no space. The whitespace after THEN was mandatory in the parser while inlineIfBody
        // already admitted the colon, so an ordinary line was underlined in the editor.
        VbDiagnosticsProvider.GetDiagnostics("Sub S()\r\n    Dim x As Long\r\n    If True Then: x = 1\r\nEnd Sub\r\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void ATrailingUnderscoreExtendsARemCommentAndCanSwallowEndSub()
    {
        // vb6.exe reports "Expected End Sub" here, and so should we — the continuation genuinely pulls the
        // next line into the remark. Asserted as a diagnostic rather than silently accepted, because this
        // is a real way to lose a procedure's closing line.
        VbDiagnosticsProvider.GetDiagnostics("Sub S()\r\n    Debug.Print 1\r\n    Rem a remark _\r\nEnd Sub\r\n")
            .Should().NotBeEmpty();
    }
}
