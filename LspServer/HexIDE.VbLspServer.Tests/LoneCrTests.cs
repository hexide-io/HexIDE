// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

namespace HexIDE.VbLspServer.Tests;

public class LoneCrTests
{
    [Fact]
    public void LexerCountsLoneCrInsideSkippedContinuationsAndComments()
    {
        const string source = "Sub T()\rDim x As _\r Long\r' comment _\r continued\rx = 1\rEnd Sub\r";
        VbDiagnosticsProvider.GetDiagnostics(source).Should().BeEmpty();
        var lexer = new VisualBasic6Lexer(new Antlr4.Runtime.AntlrInputStream(source));
        var tokens = new Antlr4.Runtime.CommonTokenStream(lexer);
        tokens.Fill();
        tokens.GetTokens().Should().Contain(t => t.Text == "x" && t.Line == 6 && t.Column == 0);
    }

    [Fact]
    public void WordsAndOccurrencesUsePhysicalLines()
    {
        const string source = "' Total\rDim Total As Long\r\nTotal = 1\n";
        VbTextHelpers.GetWordAtPosition(source, 1, 6).Should().Be("Total");
        VbTextHelpers.FindAllOccurrences(source, "Total").Select(r => (r.Start.Line, r.Start.Character))
            .Should().Equal((1, 4), (2, 0));
    }

    [Fact]
    public void CallContextStopsAtEveryLineBreak()
    {
        VbTextHelpers.FindCallContext("Sub T()\rx = Mid(a, b)\rEnd Sub", 1, 11)
            .Should().Be(("Mid", 1));
        VbTextHelpers.FindCallContext("x = Mid(a,\rb", 1, 1).FunctionName.Should().BeNull();
    }

    [Fact]
    public void ProtectedLinesAndBlankingKeepMixedTerminators()
    {
        const string source = "Attribute VB_Name = \"M\"\rSub T()\r\nAttribute T.VB_Description = \"x\"\nEnd Sub\r";
        var regions = VbProtectedRegions.Of(source);
        regions.HeaderLineCount.Should().Be(1);
        regions.IsProtected(2).Should().BeTrue();
        regions.Blank(source).Should().Be("\rSub T()\r\n\nEnd Sub\r");
    }

    [Fact]
    public void FormattingKeepsLoneCrAndUsesLineRanges()
    {
        var edits = VbFormatter.Edits("sub T()\rdim x\rend sub\r");
        edits.Should().ContainSingle();
        edits[0].Range.End.Line.Should().Be(2);
        edits[0].NewText.Should().Be("Sub T()\r    Dim x\rEnd Sub");
    }

    [Fact]
    public void FoldsUseMixedLineBreaks()
    {
        var folds = VbFoldingProvider.GetFoldings("' header\rSub T()\r\nEnd Sub\n");
        folds.Should().Contain(f => f.StartLine == 1 && f.EndLine == 2);
    }

    [Fact]
    public void DirectiveDiagnosticCoversItsLineBelowLoneCr()
    {
        const string source = "Option Explicit\r#Else\r";
        var applied = VbConditionalCompilation.Apply(source, VbCompilationContext.NoProject);
        applied.Error.Should().NotBeNull();
        var diagnostic = VbConditionalCompilation.Diagnostic(applied.Error!, source);
        diagnostic.Range.Start.Line.Should().Be(1);
        diagnostic.Range.End.Character.Should().Be(5);
    }
}
