// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using System.Text;
using static HexIDE.VbLspServer.Tests.WholeFileFixtures;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// The server accepts a whole VB6 file and leaves its header alone: no diagnostic inside it, and no formatting
/// that changes or points inside it (hexide-io/HexIDE#273 task 3.10; the language-server delta's requirement).
/// Rename and highlight are held to the same rule in <see cref="WireContractTests"/>, where they are handled.
/// </summary>
public class WholeFileAnswersTests
{
    private static string HeaderOf(string text, int headerLines) =>
        string.Concat(Lines(text).Take(headerLines).Select(l => l + "\r\n"));

    // ── Formatting ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormattingAFormLeavesItsHeaderByteForByteAndFormatsTheCode()
    {
        var formatted = VbFormatter.Format(Form);

        formatted.Should().Be(HeaderOf(Form, FormHeaderLines) +
            "Option Explicit\r\n" +
            "\r\n" +
            "Private Sub cmdOK_Click()\r\n" +
            "    Dim Caption As String\r\n" +
            "    Caption = cmdOK.Caption\r\n" +
            "End Sub\r\n");
    }

    [Fact]
    public void FormattingAClassLeavesItsHeaderAndItsMembersAttributeLinesExactlyAsTheyWere()
    {
        // The delta's scenario, and the sharper half of it: the attribute lines sit inside the property, at
        // column 0 as VB6 writes them. They are not indented as body lines, and they do not stop the body
        // after them being indented either.
        var formatted = VbFormatter.Format(Class);

        formatted.Should().Be(HeaderOf(Class, ClassHeaderLines) +
            "Option Explicit\r\n" +
            "\r\n" +
            "Public WithEvents Clock As Timer\r\n" +
            "Attribute Clock.VB_VarHelpID = -1\r\n" +
            "\r\n" +
            "Public Property Get Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"The order total\"\r\n" +
            "Attribute Total.VB_ProcData.VB_Invoke_Property = \"General\"\r\n" +
            "    total = 0\r\n" +
            "End Property\r\n");
    }

    [Fact]
    public void AFormattedCrlfFileNeedsNoFurtherFormatting()
    {
        // Every line keeps its own terminator. The formatter used to join its output with \n, so no CRLF
        // file -- which is every file VB6 writes -- was ever already formatted.
        var once = VbFormatter.Format(Class)!;

        VbFormatter.Format(once).Should().BeNull();
        VbFormatter.Edits(once).Should().BeEmpty();
    }

    [Theory]
    [InlineData(Form)]
    [InlineData(Class)]
    [InlineData(ClassWithOrphans)]
    public void NoFormattingEditCoversAProtectedLine(string text)
    {
        var regions = VbProtectedRegions.Of(text);

        var edits = VbFormatter.Edits(text);

        edits.Should().NotBeEmpty("the code under each fixture's header is deliberately unformatted");
        edits.Should().OnlyContain(e => !regions.Touches(e.Range));
    }

    [Theory]
    [InlineData(Form)]
    [InlineData(Class)]
    [InlineData(ClassWithOrphans)]
    public void TheEditsProduceExactlyWhatFormatProduces(string text)
    {
        Apply(text, VbFormatter.Edits(text)).Should().Be(VbFormatter.Format(text));
    }

    [Fact]
    public void AMembersAttributeLinesSplitTheRunOfChangedLinesAroundThem()
    {
        // Lines 15 and 18-19 of the class change; 16-17 are the property's attribute lines between them.
        var edits = VbFormatter.Edits(Class);

        edits.Select(e => (e.Range.Start.Line, e.Range.End.Line)).Should().Equal((15, 15), (18, 19));
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Form)]
    [InlineData(Class)]
    [InlineData(ClassWithOrphans)]
    public void AWholeFileWithMembersAttributeLinesParsesClean(string text)
    {
        // Measured rather than assumed: no file in the corpus carries a member's attribute lines, so until
        // this test nothing had ever handed the server's grammar one.
        VbDiagnosticsProvider.GetDiagnosticsAndTreeIncludingProtectedLines(text).Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void ADamagedHeaderRaisesNothingInTheHeader()
    {
        // The delta's "Opening a form", on the case the requirement exists for. A clean header raises
        // nothing anyway, so this damages one.
        var damaged = Form.Replace("ClientHeight    =   3000", "ClientHeight    =   = 3000", StringComparison.Ordinal);

        var unfiltered = VbDiagnosticsProvider.GetDiagnosticsAndTreeIncludingProtectedLines(damaged).Diagnostics;
        unfiltered.Should().Contain(d => d.Range.Start.Line < FormHeaderLines,
            "the grammar does see the damage; otherwise this test would prove nothing about the filter");

        VbDiagnosticsProvider.GetDiagnostics(damaged).Should().OnlyContain(d => d.Range.Start.Line >= FormHeaderLines);
    }

    [Fact]
    public void ADamagedHeaderDoesNotHideTheCodesOwnErrors()
    {
        // The case that made leaving the header's errors out insufficient. ANTLR's recovery from this damage
        // consumes the rest of the file, so the parse that meets it reports the code's syntax error nowhere;
        // leaving out the header's errors then left nothing at all.
        var damaged = Form
            .Replace("ClientHeight    =   3000", "ClientHeight    =   = 3000", StringComparison.Ordinal)
            .Replace("dim Caption as string", "dim Caption as", StringComparison.Ordinal);

        VbDiagnosticsProvider.GetDiagnosticsAndTreeIncludingProtectedLines(damaged).Diagnostics
            .Should().NotContain(d => d.Range.Start.Line == 20,
                "the grammar's own recovery hides it; otherwise this test would prove nothing about the re-parse");

        var shown = VbDiagnosticsProvider.GetDiagnostics(damaged);
        shown.Should().ContainSingle().Which.Range.Start.Should().Be(new LspPosition(20, 14));
    }

    [Fact]
    public void ADamagedHeaderStillLeavesTheCodeInTheTree()
    {
        // Symbols, folds and completion read the tree, so a header the grammar cannot read must not take the
        // code out of it either.
        var damaged = Form.Replace("ClientHeight    =   3000", "ClientHeight    =   = 3000", StringComparison.Ordinal);

        var tree = VbDiagnosticsProvider.GetDiagnosticsAndTree(damaged).Tree;

        tree.Should().NotBeNull();
        tree!.GetText().Should().Contain("cmdOK_Click");
    }

    [Fact]
    public void ADamagedMemberAttributeLineRaisesNothingOnThatLine()
    {
        var damaged = Class.Replace("VB_Description = \"The order total\"", "VB_Description = = \"x\"", StringComparison.Ordinal);

        VbDiagnosticsProvider.GetDiagnosticsAndTreeIncludingProtectedLines(damaged).Diagnostics
            .Should().Contain(d => d.Range.Start.Line == 16);
        VbDiagnosticsProvider.GetDiagnostics(damaged).Should().NotContain(d => d.Range.Start.Line == 16);
    }

    [Fact]
    public void AnOrphanedRunIsTheDevelopersTextToFormatAndToCheck()
    {
        // Task 4.1: a run VB6 does not give to the line above is ordinary text. The formatter indents the one inside
        // a function body as the body, and leaves the member's own run beside it where VB6 writes it; a damaged
        // orphan is reported, where a damaged member attribute line is not.
        var formatted = Lines(VbFormatter.Format(ClassWithOrphans)!);
        formatted[27].Should().Be("Attribute total.VB_Description = \"The sum\"", "the member's own run is protected");
        formatted[29].Should().Be("    Attribute total.VB_UserMemId = 0", "the orphan under the body statement is code");

        var damaged = ClassWithOrphans.Replace("total.VB_UserMemId = 0", "total.VB_UserMemId = = 0", StringComparison.Ordinal);
        VbDiagnosticsProvider.GetDiagnostics(damaged).Should().Contain(d => d.Range.Start.Line == 29);
    }

    [Fact]
    public void AFileTooLargeToAnalyseIsStillSaidToBeWhenItHasAHeader()
    {
        // The notice sits at (0,0), which is inside every header. It is about the file, not about line 0,
        // and filtering it would leave a developer with no diagnostics and no reason why.
        var huge = new StringBuilder(Form);
        while (huge.Length <= 400_000)
            huge.Append("' padding padding padding padding padding padding padding padding padding\r\n");

        VbDiagnosticsProvider.GetDiagnostics(huge.ToString())
            .Should().ContainSingle(d => d.Message.Contains("too large", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Applies edits the way a client does: from the last to the first, by line and character.</summary>
    private static string Apply(string text, IReadOnlyList<VbFormattingEdit> edits)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lineStarts.Add(i + 1);
        }

        var result = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Range.Start.Line).ThenByDescending(e => e.Range.Start.Character))
        {
            var start = lineStarts[edit.Range.Start.Line] + edit.Range.Start.Character;
            var end = lineStarts[edit.Range.End.Line] + edit.Range.End.Character;
            result.Remove(start, end - start).Insert(start, edit.NewText);
        }
        return result.ToString();
    }
}
