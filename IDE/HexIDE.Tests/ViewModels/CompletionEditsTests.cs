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
    public void InsertReplaceEditKeepsTheSuffixAndUndoesTogether()
    {
        var area = new TextArea { Document = new TextDocument("word suffix") };
        area.Caret.Offset = 2;
        var item = Read(""" "textEdit":{"insert":{"start":{"line":0,"character":0},"end":{"line":0,"character":2}},"replace":{"start":{"line":0,"character":0},"end":{"line":0,"character":4}},"newText":"chosen"} """);
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 0, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("chosenrd suffix");
        area.Caret.Offset.Should().Be(6);
        area.Document.UndoStack.Undo();
        area.Document.Text.Should().Be("word suffix");
    }

    [AvaloniaTheory]
    [InlineData(1, "first\n  second")]
    [InlineData(2, "first\n\t   second")]
    public void IndentationModeAppliesToPlainTextAndSnippetStops(int mode, string expected)
    {
        var area = new TextArea { Document = new TextDocument("\t word") };
        area.Caret.Offset = 6;
        var item = JsonSerializer.Deserialize<CompletionItem>(
            "{\"label\":\"test\",\"kind\":1,\"insertTextFormat\":2,\"insertTextMode\":" + mode
            + ",\"insertText\":\"first\\n  ${1:second}$0\"}", LspJsonContext.Default.Options)!;
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 2, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("\t " + expected);
        area.Selection.GetText().Should().Be("second");
        area.Selection.ReplaceSelectionWithText("changed");
        area.Document.Text.Should().Be("\t " + expected.Replace("second", "changed"));

        var plain = new TextArea { Document = new TextDocument("\t \t\t suffix") };
        plain.Caret.Offset = 2;
        var plainItem = item with { InsertTextFormat = 1, InsertText = "first\r\n  second" };
        new VbCompletionData(plainItem, (_, _) => false).Complete(plain,
            new TextSegment { StartOffset = 2, Length = 0 }, EventArgs.Empty);
        plain.Document.Text.Should().Be("\t " + expected.Replace("\n", "\r\n") + "\t\t suffix");
    }

    [AvaloniaFact]
    public void AdjustIndentationPreservesNestedMixedWhitespaceAndStops()
    {
        var area = new TextArea { Document = new TextDocument(" \tword") };
        area.Caret.Offset = 6;
        var item = new CompletionItem("nested", CompletionItemKind.Text)
        {
            InsertTextFormat = 2, InsertTextMode = 2,
            InsertText = "If x Then\n\tIf y Then\n\t\t${1:Foo}\n\tEnd If\nEnd If$0"
        };
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 2, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be(" \tIf x Then\n \t\tIf y Then\n \t\t\tFoo\n \t\tEnd If\n \tEnd If");
        area.Selection.GetText().Should().Be("Foo");
        area.Selection.ReplaceSelectionWithText("Bar");
        area.Document.Text.Should().Contain("\n \t\t\tBar\n");
    }

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
