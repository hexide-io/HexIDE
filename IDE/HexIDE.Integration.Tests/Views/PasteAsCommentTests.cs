using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

public class PasteAsCommentTests
{
    [Fact]
    public void CommandCanBeAssignedByKeymap()
    {
        HexIDE.Keymaps.CommandKeyMapping.Table.Should().ContainKey("PasteAsCommentCommand");
        HexIDE.Keymaps.CommandKeyMapping.Table["PasteAsCommentCommand"].Name.Should().Be("PasteAsCommentCommand");
    }

    [AvaloniaFact]
    public void EmptyClipboardLeavesTextAndUndoUnchanged()
    {
        var view = new CodeEditorView();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.Text = "old";
        editor.Select(0, 3);
        editor.Document.UndoStack.ClearAll();
        var insert = typeof(CodeEditorView).GetMethod("InsertAsComment", BindingFlags.Instance | BindingFlags.NonPublic);
        insert.Should().NotBeNull();
        insert!.Invoke(view, [string.Empty]);
        editor.Text.Should().Be("old");
        editor.CanUndo.Should().BeFalse();
    }

    [AvaloniaFact]
    public void BoxSelectionUsesItsOwnIndentation()
    {
        var view = new CodeEditorView();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            editor.Text = "    old\n    old";
            Dispatcher.UIThread.RunJobs();
            editor.TextArea.Selection = new RectangleSelection(editor.TextArea,
                new TextViewPosition(1, 5), new TextViewPosition(2, 8));
            editor.Document.UndoStack.ClearAll();
            var insert = typeof(CodeEditorView).GetMethod("InsertAsComment", BindingFlags.Instance | BindingFlags.NonPublic);
            insert.Should().NotBeNull();
            insert!.Invoke(view, ["first\nsecond"]);
            editor.Text.ReplaceLineEndings("\n").Should().Be("    'first\n    'second");
            editor.Undo();
            editor.Text.ReplaceLineEndings("\n").Should().Be("    old\n    old");
            editor.CanUndo.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("first\n  second", "\r\n")]
    [InlineData("first\r\n  second", "\n")]
    [InlineData("first\r  second", "\r\n")]
    public void PasteCommentsEveryLineAtIndentationAndUndoesTogether(string clipboard, string newline)
    {
        var view = new CodeEditorView();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.Text = "    old" + newline + "next";
        editor.Select(4, 3);
        editor.Document.UndoStack.ClearAll();

        var insert = typeof(CodeEditorView).GetMethod("InsertAsComment", BindingFlags.Instance | BindingFlags.NonPublic);
        insert.Should().NotBeNull();
        insert!.Invoke(view, [clipboard]);

        editor.Text.ReplaceLineEndings("\n").Should().Be("    'first\n    '  second\nnext");
        var firstLine = editor.Document.GetLineByNumber(1);
        editor.Document.GetText(firstLine.EndOffset, firstLine.DelimiterLength).Should().Be(newline);
        editor.Undo();
        editor.Text.ReplaceLineEndings("\n").Should().Be("    old\nnext");
        editor.CanUndo.Should().BeFalse();
    }
}
