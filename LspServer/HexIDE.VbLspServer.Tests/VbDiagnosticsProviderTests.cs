using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class VbDiagnosticsProviderTests
{
    [Fact]
    public void ValidSubDeclaration_ProducesNoDiagnostics()
    {
        const string code = """
            Sub Hello()
                Dim x As Integer
                x = 42
            End Sub
            """;

        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ValidFunctionDeclaration_ProducesNoDiagnostics()
    {
        const string code = """
            Function Add(a As Integer, b As Integer) As Integer
                Add = a + b
            End Function
            """;

        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void BareSubCall_InsideProcedure_ProducesNoDiagnostics()
    {
        // A bare identifier inside a procedure is a valid VBA sub call
        const string code = "Sub Main()\n    oops\nEnd Sub";

        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void InvalidCharacters_ProducesDiagnostics()
    {
        const string code = "@@@";

        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);

        diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void MismatchedEndSub_ProducesDiagnostics()
    {
        const string code = """
            Sub Hello()
                Dim x As Integer
            End Function
            """;

        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);

        diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public void Diagnostics_HaveCorrectLineNumbers()
    {
        const string code = """
            Sub Hello()
                @@@
            End Sub
            """;

        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);

        diagnostics.Should().NotBeEmpty();
        // ANTLR lines are 1-based; we convert to 0-based for LSP
        diagnostics[0].Range.Start.Line.Should().Be(1);
    }

    [Fact]
    public void EmptySource_ProducesNoDiagnostics()
    {
        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(string.Empty);

        diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Sub Foo()\nEnd Sub")]
    [InlineData("Sub Foo()\n    Dim x As String\nEnd Sub")]
    [InlineData("Sub Foo()\n    x = 1 + 2\nEnd Sub")]
    [InlineData("Sub Foo()\n    If True Then\n        MsgBox \"hi\"\n    End If\nEnd Sub")]
    public void ValidSnippets_ProduceNoDiagnostics(string code)
    {
        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);

        diagnostics.Should().BeEmpty();
    }
}
