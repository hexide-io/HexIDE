// SPDX-License-Identifier: MIT
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// A line continuation may fall INSIDE a multi-word keyword — <c>End _ Sub</c> is legal VB6, and so is
/// <c>End  Sub</c> with the words merely aligned apart. Both were rejected while the lexer spelled these
/// keywords with a single literal space (<c>'END' ' ' 'SUB'</c>).
///
/// <para>
/// These assert the LSP half of the fix. The interpreter half is covered by the conformance corpus, but
/// the corpus only ever runs against the interpreter's grammar — which is exactly how the two halves
/// drift. A user meets that drift as an editor underlining code the interpreter runs happily, so the
/// mirrored change needs its own evidence over here rather than inheriting the corpus's.
/// </para>
/// </summary>
public class SplitKeywordTests
{
    [Theory]
    [InlineData("Sub S()\r\nEnd _\r\nSub\r\n")]
    [InlineData("Sub S()\r\n    If True Then\r\n    End _\r\n    If\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    Select _\r\n    Case 1\r\n    End Select\r\nEnd Sub\r\n")]
    [InlineData("Sub S()\r\n    For x = 1 To 2\r\n        Exit _\r\n        For\r\n    Next x\r\nEnd Sub\r\n")]
    [InlineData("Option _\r\nExplicit\r\n")]
    public void AContinuationInsideAMultiWordKeyword_ProducesNoDiagnostics(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sub S()\r\nEnd  Sub\r\n")]
    [InlineData("Sub S()\r\nEnd\tSub\r\n")]
    public void ExtraWhitespaceInsideAMultiWordKeyword_ProducesNoDiagnostics(string code)
    {
        VbDiagnosticsProvider.GetDiagnostics(code).Should().BeEmpty();
    }

    [Fact]
    public void TheSeparatorDoesNotLeakAcrossALineBreak()
    {
        // KWSEP admits whitespace and continuations, NOT a bare newline. `End` on its own line followed by
        // `Sub` is two statements, and widening the separator must not have quietly joined them into one.
        VbDiagnosticsProvider.GetDiagnostics("Sub S()\r\nEnd\r\nSub\r\n").Should().NotBeEmpty();
    }
}
