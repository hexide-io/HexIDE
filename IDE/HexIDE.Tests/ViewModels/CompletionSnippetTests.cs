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

    [Fact]
    public void ExponentiallyLinkedDefaultsRefuseOutputOverOneMiB()
    {
        var source = string.Concat(Enumerable.Range(1, 21)
            .Select(number => "${" + number + ":$" + (number + 1) + "$" + (number + 1) + "}")) + "${22:x}";
        CompletionSnippet.Parse(source).Should().BeNull();
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
    [InlineData("${1:unclosed")]
    public void UnsupportedOrMalformedSyntaxRefusesTheItem(string snippet)
    {
        Complete(snippet).Document.Text.Should().Be("word");
    }

    [AvaloniaFact]
    public void NestedPlaceholdersNavigateFromOuterToInner()
    {
        var area = Complete("${1:Dim ${2:x}}$0");
        area.Document.Text.Should().Be("Dim x");
        area.Selection.GetText().Should().Be("Dim x");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("x");
        area.Selection.ReplaceSelectionWithText("entry");
        Key(area, Avalonia.Input.Key.Tab);
        area.Document.Text.Should().Be("Dim entry");
        area.Caret.Offset.Should().Be(9);
    }

    [AvaloniaFact]
    public void NestedMultilineStopsFollowAdjustedIndentation()
    {
        var area = new TextArea { Document = new TextDocument("\t word") };
        area.Caret.Offset = 6;
        var item = new CompletionItem("nested", CompletionItemKind.Text,
            InsertTextFormat: 2, InsertText: "${1:If x Then\n  ${2:Foo}}$0") { InsertTextMode = 2 };
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 2, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("\t If x Then\n\t   Foo");
        area.Selection.GetText().Should().Be("If x Then\n\t   Foo");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("Foo");
        area.Selection.ReplaceSelectionWithText("Bar");
        area.Document.Text.Should().Be("\t If x Then\n\t   Bar");
    }

    [AvaloniaFact]
    public void VariablesReadTheOriginalSelectionLineWordAndLineNumbers()
    {
        var area = new TextArea { Document = new TextDocument("before\nword rest") };
        area.Caret.Offset = 9;
        area.Selection = Selection.Create(area, 7, 11);
        var item = new CompletionItem("variables", CompletionItemKind.Text, InsertTextFormat: 2,
            InsertText: "$TM_SELECTED_TEXT|${TM_CURRENT_LINE}|$TM_CURRENT_WORD|$TM_LINE_INDEX|$TM_LINE_NUMBER");
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 7, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("before\nword|word rest|word|1|2 rest");
    }

    [AvaloniaFact]
    public void UnsetVariablesExpandDefaultsAndUnknownNamesBecomeStops()
    {
        var area = Complete("${TM_FILENAME:${1:fallback}} $UNKNOWN $0");
        area.Document.Text.Should().Be("fallback UNKNOWN ");
        area.Selection.GetText().Should().Be("fallback");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("UNKNOWN");
        area.Selection.ReplaceSelectionWithText("named");
        area.Document.Text.Should().Be("fallback named ");
    }

    [AvaloniaFact]
    public void FileVariablesUseTheLiveDocumentUriAndDoNotExpandItsContents()
    {
        var area = new TextArea { Document = new TextDocument("word") };
        var path = Path.Combine(Path.GetTempPath(), "snippet $1", "example.test.bas");
        string? uri = "untitled:example.bas";
        var item = new CompletionItem("file", CompletionItemKind.Text, InsertTextFormat: 2,
            InsertText: "$TM_FILENAME|${TM_FILENAME_BASE}|$TM_DIRECTORY|$TM_FILEPATH");
        var data = new VbCompletionData(item, (_, _) => false, () => uri);
        uri = new Uri(path).AbsoluteUri;
        data.Complete(area, new TextSegment { StartOffset = 0, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be("example.test.bas|example.test|" + Path.GetDirectoryName(path) + "|" + path);
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void UnsetKnownVariablesUseEmptyTextOrTheirNestedDefault()
    {
        Complete("$TM_FILENAME|${TM_DIRECTORY}|$TM_FILEPATH|$TM_SELECTED_TEXT|${TM_FILENAME_BASE:base}")
            .Document.Text.Should().Be("||||base");
        Complete("${TM_SELECTED_TEXT:default}").Document.Text.Should().Be("default");
    }

    [AvaloniaFact]
    public void AResolvedVariableSuppressesItsDefaultStops()
    {
        var area = Complete("${TM_CURRENT_WORD:${1:unused}}${2:used}$0");
        area.Document.Text.Should().Be("wordused");
        area.Selection.GetText().Should().Be("used");
        Key(area, Avalonia.Input.Key.Tab);
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ReplacingAnOuterPlaceholderRemovesItsNestedStop()
    {
        var area = Complete("${1:Dim ${2:x}} ${3:next}$0");
        area.Selection.ReplaceSelectionWithText("replacement");
        area.Document.Text.Should().Be("replacement next");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("next");
        Key(area, Avalonia.Input.Key.Tab);
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void NestedDefaultsLinkReferencesBeforeAndAfterTheirDefinition()
    {
        var area = Complete("$1 ${1:Dim ${2:x}} $2$0");
        area.Document.Text.Should().Be("Dim x Dim x x");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("x");
        area.Selection.ReplaceSelectionWithText("entry");
        area.Document.Text.Should().Be("Dim entry Dim entry entry");
    }

    [AvaloniaFact]
    public void EditingAnInnerFieldWithTheSameExtentKeepsItsDescendants()
    {
        var area = Complete("${1:${2:${3:x}}} ${4:next}$0");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.ReplaceSelectionWithText("y");
        area.Document.Text.Should().Be("y next");
        Key(area, Avalonia.Input.Key.Tab, KeyModifiers.Shift);
        area.Selection.GetText().Should().Be("y");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("y");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("y");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("next");
    }

    [AvaloniaFact]
    public void ReplacingAParentRetiresItsCopyOfAnOutsideField()
    {
        var area = Complete("${2:x} ${1:Dim $2} $1$0");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.ReplaceSelectionWithText("entry");
        area.Document.Text.Should().Be("entry Dim entry Dim entry");
        Key(area, Avalonia.Input.Key.Tab, KeyModifiers.Shift);
        area.Selection.ReplaceSelectionWithText("replacement");
        area.Document.Text.Should().Be("entry replacement replacement");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("entry");
        area.Selection.ReplaceSelectionWithText("last");
        area.Document.Text.Should().Be("last replacement replacement");
    }

    [AvaloniaTheory]
    [InlineData("${1:a ${2:x}} $2", "q x")]
    [InlineData("${1:a ${2:x}} ${3:b $2}", "q b x")]
    public void ReplacingParentPromotesSurvivingChild(string snippet, string expected)
    {
        var area = Complete(snippet);
        area.Selection.ReplaceSelectionWithText("q");
        area.Document.Text.Should().Be(expected);
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("x");
        area.Selection.ReplaceSelectionWithText("y");
        area.Document.Text.Should().Be(expected[..^1] + "y");
    }

    [AvaloniaTheory]
    [InlineData("${1:Dim ${2:x}}", 4, 1, "y", "y")]
    [InlineData("${1:foo($2)}", 4, 1, "", "")]
    public void EditingWithinOrBesideChildKeepsIt(string snippet, int offset, int length, string value, string child)
    {
        var area = Complete(snippet);
        area.Selection = Selection.Create(area, offset, offset + length);
        area.Selection.ReplaceSelectionWithText(value);
        Key(area, Avalonia.Input.Key.Tab);
        area.StackedInputHandlers.Should().NotBeEmpty();
        area.Selection.GetText().Should().Be(child);
    }

    [AvaloniaFact]
    public void TypingInClearedChildDoesNotExtendItsSibling()
    {
        var area = Complete("${1:${2:a}${3:b}}");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.ReplaceSelectionWithText("");
        area.Selection.ReplaceSelectionWithText("z");
        area.Document.Text.Should().Be("zb");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("b");
    }

    [AvaloniaFact]
    public void TouchingEmptyCopiesMirrorOnceEach()
    {
        var area = Complete("${3:$1$1}");
        area.Selection.ReplaceSelectionWithText("z");
        area.Document.Text.Should().Be("zz");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("zz");
    }

    [AvaloniaFact]
    public void AdjustIndentationPreservesMultilineVariableValues()
    {
        var area = new TextArea { Document = new TextDocument("  word\nselected") };
        area.Caret.Offset = 6;
        area.Selection = Selection.Create(area, 2, area.Document.TextLength);
        var item = new CompletionItem("variable", CompletionItemKind.Text, InsertTextFormat: 2,
            InsertText: "${1:begin\n$TM_SELECTED_TEXT\n${2:end}}") { InsertTextMode = 2 };
        new VbCompletionData(item, (_, _) => false).Complete(area,
            new TextSegment { StartOffset = 2, Length = area.Document.TextLength - 2 }, EventArgs.Empty);
        area.Document.Text.Should().Be("  begin\n  word\nselected\n  end");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("end");
    }

    [AvaloniaFact]
    public void DotfileBaseKeepsItsLeadingDot()
    {
        var area = new TextArea { Document = new TextDocument("word") };
        var item = new CompletionItem("file", CompletionItemKind.Text, InsertTextFormat: 2,
            InsertText: "${TM_FILENAME_BASE:default}");
        new VbCompletionData(item, (_, _) => false,
            () => new Uri(Path.Combine(Path.GetTempPath(), ".gitignore")).AbsoluteUri).Complete(area,
            new TextSegment { StartOffset = 0, Length = 4 }, EventArgs.Empty);
        area.Document.Text.Should().Be(".gitignore");
    }

    [AvaloniaTheory]
    [InlineData("$ ")]
    [InlineData("$)")]
    [InlineData("$")]
    public void DollarWithoutAnExpressionIsLiteral(string source)
    {
        Complete(source).Document.Text.Should().Be(source);
    }

    [AvaloniaFact]
    public void NestedInsertionUndoesTogetherAndEscapeEndsItsLinks()
    {
        var undone = Complete("${1:Dim ${2:x}} $1$0");
        undone.Document.Text.Should().Be("Dim x Dim x");
        undone.Document.UndoStack.Undo();
        undone.Document.Text.Should().Be("word");
        undone.StackedInputHandlers.Should().BeEmpty();
        var area = Complete("${1:Dim ${2:x}} $1$0");
        Key(area, Avalonia.Input.Key.Tab);
        Key(area, Avalonia.Input.Key.Escape);
        area.Document.Replace(4, 1, "y");
        area.Document.Text.Should().Be("Dim y Dim x");
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void RetiringAnEarlierNumberedChildKeepsTheNextStop()
    {
        var area = Complete("${2:Dim ${1:x}} ${3:next}$0");
        area.Selection.GetText().Should().Be("x");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("Dim x");
        area.Selection.ReplaceSelectionWithText("replacement");
        Key(area, Avalonia.Input.Key.Tab);
        area.Selection.GetText().Should().Be("next");
        Key(area, Avalonia.Input.Key.Tab, KeyModifiers.Shift);
        area.Selection.GetText().Should().Be("replacement");
        Key(area, Avalonia.Input.Key.Tab);
        Key(area, Avalonia.Input.Key.Tab);
        area.StackedInputHandlers.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("${TM_FILENAME/(.*)/$1/}")]
    [InlineData("${TM_FILENAME:ignored ${TM_FILEPATH/(.*)/$1/}}")]
    public void VariableTransformsRefuseAllEdits(string snippet)
    {
        Complete(snippet, ",\"additionalTextEdits\":[{\"range\":{\"start\":{\"line\":0,\"character\":4},\"end\":{\"line\":0,\"character\":4}},\"newText\":\"extra\"}]")
            .Document.Text.Should().Be("word");
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
