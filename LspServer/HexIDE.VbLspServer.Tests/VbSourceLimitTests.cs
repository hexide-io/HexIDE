// SPDX-License-Identifier: MIT
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// Measured edges: docs/vb6-fidelity-oracle/long-lines-line-continuations-and-long-expressions.md (#3202).
public class VbSourceLimitTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void ExplicitContinuationsWarnOnTheTwentyFifth(string newline)
    {
        string Module(int count) => "Sub S()" + newline + "v = "
            + string.Concat(Enumerable.Repeat("1 + _" + newline, count)) + "1" + newline + "End Sub" + newline;
        VbDiagnosticsProvider.GetDiagnostics(Module(24)).Should().BeEmpty();
        var diagnostic = VbDiagnosticsProvider.GetDiagnostics(Module(25)).Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be(2);
        diagnostic.Message.Should().Be("VB6 refuses this logical line: Too many line continuations. HexIDE runs it.");
        diagnostic.Range.Should().Be(new LspRange(new LspPosition(25, 4), new LspPosition(25, 5)));
        VbDiagnosticsProvider.GetDiagnostics(Module(26)).Should().Equal(diagnostic);
        VbDiagnosticsProvider.GetDiagnostics(Module(24) + Module(24)).Should().BeEmpty();
    }

    [Fact]
    public void PhysicalLineWarnsOnlyPast1023()
    {
        string Module(int length) => "Sub S()\n" + "v = 1".PadRight(length - 6) + "+ 1000\nEnd Sub\n";
        VbDiagnosticsProvider.GetDiagnostics(Module(1023)).Should().BeEmpty();
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(Module(1024));
        tree.Should().NotBeNull();
        var diagnostic = diagnostics.Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be(2);
        diagnostic.Range.Should().Be(new LspRange(new LspPosition(1, 0), new LspPosition(1, 1024)));
        diagnostic.Message.Should().Be("VB6 reads physical lines over 1,023 characters in 1,023-character pieces. A cut inside a number or string is a syntax error there; in some layouts VB6 silently drops the line's end. HexIDE keeps the whole line.");
    }

    [Fact]
    public void PhysicalPiecesCountTowardsContinuationLimit()
    {
        string Module(int length) => "Sub S()\n" + "v = 1".PadRight(length - 3) + "+ 1\nEnd Sub\n";
        VbDiagnosticsProvider.GetDiagnostics(Module(25 * 1023))
            .Where(d => d.Message.Contains("Too many line continuations")).Should().BeEmpty();
        var diagnostic = VbDiagnosticsProvider.GetDiagnostics(Module(25 * 1023 + 1))
            .Where(d => d.Message.Contains("Too many line continuations")).Should().ContainSingle().Subject;
        diagnostic.Range.Start.Should().Be(new LspPosition(1, 25 * 1023));
        diagnostic.Severity.Should().Be(2);
    }

    [Fact]
    public void PhysicalPiecesAndExplicitContinuationsShareTheBudget()
    {
        string Module(int pieces) => "Sub S()\n" + "v = 1 +".PadRight(pieces * 1023 - 2)
            + " _\n1\nEnd Sub\n";
        VbDiagnosticsProvider.GetDiagnostics(Module(24))
            .Where(d => d.Message.Contains("Too many line continuations")).Should().BeEmpty();
        var diagnostic = VbDiagnosticsProvider.GetDiagnostics(Module(25))
            .Where(d => d.Message.Contains("Too many line continuations")).Should().ContainSingle().Subject;
        diagnostic.Range.Start.Should().Be(new LspPosition(1, 25 * 1023 - 1));
    }
}
