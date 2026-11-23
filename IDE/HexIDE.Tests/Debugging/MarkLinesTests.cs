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

    private static int? Map(TextDocument document, int? mark, DocumentChangeEventArgs change) =>
        mark is { } line ? MarkLines.Remap(line, change.Offset, change.RemovalLength,
            HexIDE.Runtime.Serialization.FormCodeText.LineBreaks(change.RemovedText.Text),
            HexIDE.Runtime.Serialization.FormCodeText.LineBreaks(change.InsertedText.Text), n =>
            {
                var before = document.GetLineByNumber(n);
                var start = before.Offset;
                while (start < before.EndOffset && char.IsWhiteSpace(document.GetCharAt(start))) start++;
                return (before.Offset, start, before.EndOffset, before.Offset + before.TotalLength);
            }) : null;
}
