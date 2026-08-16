// SPDX-License-Identifier: MIT
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class VbFoldingProviderTests
{
    // ── Sub / Function ────────────────────────────────────────────────────────

    [Fact]
    public void SingleSub_ProducesOneFold()
    {
        const string code = """
            Sub Hello()
                Dim x As Integer
                x = 42
            End Sub
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(1);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(3);
    }

    [Fact]
    public void SingleFunction_ProducesOneFold()
    {
        const string code = """
            Function Add(a As Integer, b As Integer) As Integer
                Add = a + b
            End Function
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(1);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(2);
    }

    [Fact]
    public void TwoSubs_ProduceTwoFolds()
    {
        const string code = """
            Sub First()
                Dim x As Integer
            End Sub

            Sub Second()
                Dim y As Integer
            End Sub
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(2);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(2);
        folds[1].StartLine.Should().Be(4);
        folds[1].EndLine.Should().Be(6);
    }

    // ── Nesting ───────────────────────────────────────────────────────────────

    [Fact]
    public void ForLoopInsideSub_ProducesTwoFolds()
    {
        const string code = """
            Sub DoWork()
                For i = 1 To 10
                    Debug.Print i
                Next i
            End Sub
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(2);
        // Sorted by startLine
        folds[0].StartLine.Should().Be(0); // Sub
        folds[0].EndLine.Should().Be(4);
        folds[1].StartLine.Should().Be(1); // For
        folds[1].EndLine.Should().Be(3);
    }

    [Fact]
    public void NestedIfInsideSub_ProducesTwoFolds()
    {
        const string code = """
            Sub Check()
                If x > 0 Then
                    Debug.Print "positive"
                End If
            End Sub
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(2);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(4);
        folds[1].StartLine.Should().Be(1);
        folds[1].EndLine.Should().Be(3);
    }

    // ── If / End If ───────────────────────────────────────────────────────────

    [Fact]
    public void MultilineIf_ProducesOneFold()
    {
        const string code = """
            If condition Then
                DoSomething
            End If
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(1);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(2);
    }

    [Fact]
    public void SingleLineIf_ProducesNoFold()
    {
        const string code = "If x > 0 Then DoSomething";

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().BeEmpty();
    }

    // ── Property ─────────────────────────────────────────────────────────────

    [Fact]
    public void PropertyGet_ProducesOneFold()
    {
        const string code = """
            Property Get Name() As String
                Name = _name
            End Property
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(1);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(2);
    }

    // ── Select Case ───────────────────────────────────────────────────────────

    [Fact]
    public void SelectCase_ProducesOneFold()
    {
        const string code = """
            Select Case x
                Case 1
                    Debug.Print "one"
                Case Else
                    Debug.Print "other"
            End Select
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(1);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(5);
    }

    // ── Enum / Type ───────────────────────────────────────────────────────────

    [Fact]
    public void Enum_ProducesOneFold()
    {
        const string code = """
            Public Enum Color
                Red
                Green
                Blue
            End Enum
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(1);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(4);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void SingleLineSub_Folded_CollapseRange_Is_AtLeastTwoLines()
    {
        // A proc with just its header + End Sub (minimum foldable)
        const string code = """
            Sub Empty()
            End Sub
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        // startLine=0, endLine=1 → span of 1 → ≥1 → should be emitted
        folds.Should().HaveCount(1);
    }

    [Fact]
    public void EmptySource_ProducesNoFolds()
    {
        var folds = VbFoldingProvider.GetFoldings("");
        folds.Should().BeEmpty();
    }

    [Fact]
    public void OrphanedCloser_ProducesNoFold()
    {
        const string code = """
            End Sub
            """;

        var folds = VbFoldingProvider.GetFoldings(code);
        folds.Should().BeEmpty();
    }

    [Fact]
    public void WithBlock_ProducesOneFold()
    {
        const string code = """
            With myObj
                .Name = "test"
                .Value = 42
            End With
            """;

        var folds = VbFoldingProvider.GetFoldings(code);

        folds.Should().HaveCount(1);
        folds[0].StartLine.Should().Be(0);
        folds[0].EndLine.Should().Be(3);
    }
}
