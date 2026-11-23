using AvaloniaEdit.Document;
using HexIDE.Debugging;
using HexIDE.Tests.ViewModels;

namespace HexIDE.Tests.Debugging;

public class MarkLinesTests
{
    // All eight rows, including the control, from
    // docs/vb6-fidelity-oracle/a-breakpoint-through-edits-above-it.md (#1053).
    [AvaloniaTheory]
    [InlineData("control", 6)]
    [InlineData("insert", 7)]
    [InlineData("delete above", 5)]
    [InlineData("paste", 7)]
    [InlineData("undo", 6)]
    [InlineData("enter", 7)]
    [InlineData("delete own", null)]
    [InlineData("join", null)]
    public void MeasuredEditShapes(string edit, int? expected)
    {
        var document = new TextDocument(BreakpointEditTests.Program.ReplaceLineEndings("\r\n"));
        int? mark = 6;
        document.Changing += (_, change) => mark = Map(document, mark, change);

        BreakpointEditTests.Edit(document, edit);
        if (edit == "undo") document.UndoStack.Undo();

        mark.Should().Be(expected);
        if (mark is { } line)
            document.GetText(document.GetLineByNumber(line)).Trim().Should().Be("L \"two\"");
    }

    // VB6 SP6 bookmark measurements for #5334 (oracle PR #5387).
    [AvaloniaTheory]
    [InlineData("control", 6)]
    [InlineData("insert", 7)]
    [InlineData("delete above", 5)]
    [InlineData("paste", 7)]
    [InlineData("undo", 6)]
    [InlineData("enter", 7)]
    [InlineData("delete own", null)]
    [InlineData("join", 5)]
    public void MeasuredBookmarkEditShapes(string edit, int? expected)
    {
        var document = new TextDocument(BreakpointEditTests.Program);
        int? mark = 6;
        document.Changing += (_, change) => mark = Map(document, mark, change, bookmark: true);

        BreakpointEditTests.Edit(document, edit);
        if (edit == "undo") document.UndoStack.Undo();

        mark.Should().Be(expected);
        if (mark is { } line)
            document.GetText(document.GetLineByNumber(line)).Trim().Should().Be("L \"two\"");
    }

    // HexIDE's #1053 rule for join shapes outside the measured bookmark rows.
    [AvaloniaTheory]
    [InlineData(true, null)]
    [InlineData(false, 6)]
    public void OtherBookmarkJoinsUseTheBreakpointRule(bool cutsIntoStatement, int? expected)
    {
        var document = new TextDocument(BreakpointEditTests.Program);
        int? mark = 6;
        document.Changing += (_, change) => mark = Map(document, mark, change, bookmark: true);
        var two = document.GetLineByNumber(6);

        if (cutsIntoStatement)
        {
            var one = document.GetLineByNumber(5);
            document.Remove(one.Offset + 4, two.Offset - one.Offset + 1);
        }
        else
            document.Remove(two.EndOffset, document.GetLineByNumber(7).Offset - two.EndOffset);

        mark.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void DeletingWholeLinesUpToTheMarkedLinesBeginningKeepsItsMark(string ending)
    {
        var document = new TextDocument(BreakpointEditTests.Program.ReplaceLineEndings(ending));
        int? mark = 6;
        document.Changing += (_, change) => mark = Map(document, mark, change);
        var above = document.GetLineByNumber(5);

        document.Remove(above.Offset, above.TotalLength);

        mark.Should().Be(5);
    }

    [AvaloniaFact]
    public void InsertingAfterTheStatementStartKeepsItsLine()
    {
        var document = new TextDocument(BreakpointEditTests.Program);
        int? mark = 6;
        document.Changing += (_, change) => mark = Map(document, mark, change);

        document.Insert(document.GetLineByNumber(6).EndOffset, "\n");

        mark.Should().Be(6);
    }

    [AvaloniaFact]
    public void ABlankLinesStartIsItsEnd()
    {
        var document = new TextDocument("one\n   \nthree");
        int? mark = 2;
        document.Changing += (_, change) => mark = Map(document, mark, change);

        document.Insert(document.GetLineByNumber(2).EndOffset, "\n");

        mark.Should().Be(3);
    }

    private static int? Map(TextDocument document, int? mark, DocumentChangeEventArgs change, bool bookmark = false)
    {
        if (mark is not { } line)
            return null;
        var removed = HexIDE.Runtime.Serialization.FormCodeText.LineBreaks(change.RemovedText.Text);
        var inserted = HexIDE.Runtime.Serialization.FormCodeText.LineBreaks(change.InsertedText.Text);
        return bookmark
            ? MarkLines.RemapBookmark(line, change.Offset, change.RemovalLength, removed, inserted, ReadLine)
            : MarkLines.Remap(line, change.Offset, change.RemovalLength, removed, inserted, ReadLine);

        (int Begin, int Statement, int End, int Next) ReadLine(int number)
        {
            var before = document.GetLineByNumber(number);
            var start = before.Offset;
            while (start < before.EndOffset && char.IsWhiteSpace(document.GetCharAt(start))) start++;
            return (before.Offset, start, before.EndOffset, before.Offset + before.TotalLength);
        }
    }
}
