using System.Text.Json;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Primitives;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using HexIDE.Forms.Views;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.ViewModels;

public class CompletionSnippetTests
{
    private static TextArea Complete(string snippet, string additional = "", TextArea? area = null)
    {
        area ??= new TextArea { Document = new TextDocument("word") };
        var json = "{\"label\":\"snippet\",\"kind\":1,\"insertTextFormat\":2,\"insertText\":"
                   + JsonSerializer.Serialize(snippet) + additional + "}";
        var item = JsonSerializer.Deserialize<CompletionItem>(json, LspJsonContext.Default.Options)!;
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 0, Length = 4 }, EventArgs.Empty);
        return area;
    }

    private static void Key(TextArea area, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        area.StackedInputHandlers.First().OnPreviewKeyDown(args);
        args.Handled.Should().BeTrue();
    }

    [AvaloniaFact]
    public void DefaultsAreSelectedAndLinkedWhileAdditionalEditsRemainLiteral()
    {
        var area = Complete("Dim ${1:Item}\nFor Each $1 In ${2:c}\n$0Next",
            ",\"additionalTextEdits\":[{\"range\":{\"start\":{\"line\":0,\"character\":4},\"end\":{\"line\":0,\"character\":4}},\"newText\":\"$1\"}]");
        area.Document.Text.Should().Be("Dim Item\nFor Each Item In c\nNext$1");
        area.Selection.GetText().Should().Be("Item");
        area.Selection.ReplaceSelectionWithText("entry");
        area.Document.Text.Should().Be("Dim entry\nFor Each entry In c\nNext$1");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("c");
        Key(area, Avalonia.Input.Key.Tab);
        area.Caret.Offset.Should().Be(area.Document.Text.IndexOf("Next", StringComparison.Ordinal));
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void NumericOrderBackwardTabAndBracedEmptyStops()
    {
        var area = Complete("${2:second} ${1:first} ${3}$0");
        area.Selection.GetText().Should().Be("first");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("second");
        Key(area, Avalonia.Input.Key.Tab, KeyModifiers.Shift);
        area.Selection.GetText().Should().Be("first");
        Key(area, Avalonia.Input.Key.Tab);
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.Length.Should().Be(0);
        Key(area, Avalonia.Input.Key.Tab);
        area.Caret.Offset.Should().Be(area.Document.TextLength);
    }

    [AvaloniaFact]
    public void EscapesAndMultilineWhitespaceArePreservedAndInsertionUndoesTogether()
    {
        var area = Complete("\\$ ${1:value}\r\n\t\\} \\\\ $0");
        area.Document.Text.Should().Be("$ value\r\n\t} \\ ");
        area.Document.UndoStack.Undo();
        area.Document.Text.Should().Be("word");
        area.Document.UndoStack.CanUndo.Should().BeFalse();
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ChoosingAValueUpdatesLinkedOccurrencesAndTabFinishes()
    {
        var theme = new Avalonia.Themes.Simple.SimpleTheme();
        Application.Current!.Styles.Add(theme);
        var editorTheme = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Simple/AvaloniaEdit.xaml")
        };
        Application.Current.Styles.Add(editorTheme);
        var editor = new AvaloniaEdit.TextEditor { Text = "word" };
        var area = editor.TextArea;
        var window = new Window { Content = editor, Width = 500, Height = 300 };
        window.Show();
        window.UpdateLayout();
        editor.ApplyTemplate();
        area.ApplyTemplate();
        window.UpdateLayout();
        try
        {
            TopLevel.GetTopLevel(area.TextView).Should().BeSameAs(window);
            CompletionWindow? picker = null;
            using var opened = Popup.IsOpenProperty.Changed.AddClassHandler<CompletionWindow>((popup, _) =>
            {
                if (popup.IsOpen) picker = popup;
            });
            area.Caret.Offset = 4;
            area.Focus();
            var completion = new CompletionWindow(area) { StartOffset = 0, EndOffset = 4 };
            completion.CompletionList.CompletionData.Add(new VbCompletionData(new CompletionItem("zsnip", CompletionItemKind.Text,
                InsertTextFormat: 2, InsertText: "${1|one,two|} $1$0"), (_, _) => false));
            completion.Show();
            completion.CompletionList.SelectedItem = completion.CompletionList.CompletionData[0];
            window.KeyPress(Avalonia.Input.Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Avalonia.Input.Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            picker.Should().NotBeNull();
            picker!.IsOpen.Should().BeTrue();
            picker.CompletionList.IsVisible.Should().BeTrue();
            picker!.CompletionList.CompletionData.Select(item => item.Text).Should().Equal("one", "two");
            picker.CompletionList.SelectedItem = picker.CompletionList.CompletionData[1];
            picker.CompletionList.RequestInsertion(EventArgs.Empty);
            picker.IsOpen.Should().BeFalse();
            area.Document.Text.Should().Be("two two");
            Key(area, Avalonia.Input.Key.Tab);
            area.Caret.Offset.Should().Be(7);
            area.StackedInputHandlers.Should().BeEmpty();
        }
        finally
        {
            window.Close();
            Application.Current.Styles.Remove(editorTheme);
            Application.Current.Styles.Remove(theme);
        }
    }

    [AvaloniaFact]
    public void TabAfterRoutedTypingFinishesWithoutACompletionList()
    {
        var theme = new Avalonia.Themes.Simple.SimpleTheme();
        Application.Current!.Styles.Add(theme);
        var editorTheme = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Simple/AvaloniaEdit.xaml")
        };
        Application.Current.Styles.Add(editorTheme);
        var editor = new AvaloniaEdit.TextEditor { Text = "word" };
        var window = new Window { Content = editor, Width = 500, Height = 300 };
        window.Show();
        try
        {
            window.UpdateLayout();
            editor.TextArea.Focus();
            Complete("${1:one} $1$0", area: editor.TextArea);
            window.KeyTextInput("two");
            Dispatcher.UIThread.RunJobs();
            editor.Text.Should().Be("two two");
            window.KeyPress(Avalonia.Input.Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Avalonia.Input.Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            editor.TextArea.Caret.Offset.Should().Be(7);
            editor.TextArea.StackedInputHandlers.Should().BeEmpty();
        }
        finally
        {
            window.Close();
            Application.Current.Styles.Remove(editorTheme);
            Application.Current.Styles.Remove(theme);
        }
    }

    [AvaloniaTheory]
    [InlineData("${1|one,two,three|} $1$0", "one one")]
    [InlineData(@"${1|a\,b,c\|d,e\\f|} $1", "a,b a,b")]
    [InlineData("${1|,two|} $1", " ")]
    public void ChoicesInsertTheirFirstValueAndLinkReferences(string snippet, string expected)
    {
        var area = Complete(snippet);
        area.Document.Text.Should().Be(expected);
        area.Selection.ReplaceSelectionWithText("chosen");
        area.Document.Text.Should().Be("chosen chosen");
    }

    [AvaloniaTheory]
    [InlineData("${1|one,two}")]
    [InlineData("$TM_FILENAME")]
    [InlineData("${1:unclosed")]
    [InlineData("${1:outer ${2:inner}}")]
    public void UnsupportedOrMalformedSyntaxRefusesTheItem(string snippet)
    {
        Complete(snippet).Document.Text.Should().Be("word");
    }

    [AvaloniaFact]
    public void AnEmptyStopMovesTheFinalCaretWithTypedText()
    {
        var area = Complete("$1$0");
        area.Selection.ReplaceSelectionWithText("typed");
        Key(area, Avalonia.Input.Key.Tab);
        area.Document.Text.Should().Be("typed");
        area.Caret.Offset.Should().Be(5);
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void AReferenceBeforeItsDefaultIsLinked()
    {
        var area = Complete("$1 ${1:later}$0");
        area.Document.Text.Should().Be("later later");
        area.Selection.GetText().Should().Be("later");
        area.Selection.ReplaceSelectionWithText("new");
        area.Document.Text.Should().Be("new new");
    }

    [AvaloniaFact]
    public void FinalStopAlonePlacesCaretAndEscapeEndsLinkedEditing()
    {
        var final = Complete("before$0after");
        final.Caret.Offset.Should().Be(6);
        var area = Complete("${1:x} $1");
        Key(area, Avalonia.Input.Key.Escape);
        area.StackedInputHandlers.Should().BeEmpty();
        area.Document.Replace(0, 1, "y");
        area.Document.Text.Should().Be("y x");
    }
}
