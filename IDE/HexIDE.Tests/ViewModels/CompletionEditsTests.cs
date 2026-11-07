using System.Text.Json;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using HexIDE.Forms.Views;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.ViewModels;

public class CompletionEditsTests
{
    private static CompletionItem Read(string fields) => JsonSerializer.Deserialize<CompletionItem>(
        "{\"label\":\"fallback\",\"kind\":1,\"insertText\":\"ignored\"," + fields + "}",
        LspJsonContext.Default.Options)!;

    [AvaloniaFact]
    public void TextEditOverridesTheWordAndInsertText()
    {
        var area = new TextArea { Document = new TextDocument("old suffix") };
        var item = Read(""" "textEdit":{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":10}},"newText":"chosen"} """);
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 0, Length = 3 }, EventArgs.Empty);
        area.Document.Text.Should().Be("chosen");
        area.Caret.Offset.Should().Be(6);
    }

    [AvaloniaFact]
    public void AdditionalEditsUseOriginalOffsetsAndUndoTogether()
    {
        var area = new TextArea { Document = new TextDocument("header\nword\ntail") };
        var item = Read(""" "additionalTextEdits":[{"range":{"start":{"line":2,"character":0},"end":{"line":2,"character":4}},"newText":"end"},{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}},"newText":"prefix\n"}] """);
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 7, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("prefix\nheader\nignored\nend");
        area.Caret.Offset.Should().Be(21);
        area.Document.UndoStack.Undo();
        area.Document.Text.Should().Be("header\nword\ntail");
        area.Document.UndoStack.CanUndo.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(0, 1)]
    [InlineData(9, 10)]
    public void OverlappingOrOutOfBoundsEditsRefuseTheWholeCompletion(int start, int end)
    {
        var area = new TextArea { Document = new TextDocument("word") };
        var item = Read($$$""" "additionalTextEdits":[{"range":{"start":{"line":0,"character":{{{start}}}},"end":{"line":0,"character":{{{end}}}}},"newText":"bad"}] """);
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 0, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("word");
    }

    [AvaloniaFact]
    public void AProtectedAdditionalEditRefusesTheWholeCompletion()
    {
        var area = new TextArea { Document = new TextDocument("header\nword") };
        var item = Read(""" "additionalTextEdits":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}},"newText":"prefix"}] """);
        new VbCompletionData(item, (offset, _) => offset < 7).Complete(area,
            new TextSegment { StartOffset = 7, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("header\nword");
    }
}
