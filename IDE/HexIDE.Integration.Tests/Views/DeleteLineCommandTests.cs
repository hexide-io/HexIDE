using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Input;
using AvaloniaEdit;
using HexIDE.Forms.Views;
using HexIDE.Keymaps;

namespace HexIDE.Integration.Tests.Views;

public class DeleteLineCommandTests
{
    [AvaloniaFact]
    public void DeleteLineIsRemappableAndLibraryRedoHasNoFallbackGesture()
    {
        CommandKeyMapping.Table.Should().ContainKey("DeleteLineCommand");
        var command = CommandKeyMapping.Table["DeleteLineCommand"];
        command.Gestures.Should().Equal(new KeyGesture(Key.Y, ApplicationCommands.UndoCommand.Gestures[0].KeyModifiers));
        var view = new CodeEditorView();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.DefaultInputHandler.CommandBindings.Should().NotContain(
            binding => binding.Command == AvaloniaEdit.ApplicationCommands.Redo);
        editor.Text = "first\nsecond\n";
        editor.Document.UndoStack.ClearAll();
        var originalGestures = command.Gestures.ToArray();
        var window = new Window { Content = view };
        window.Show();
        try
        {
            editor.TextArea.Focus();
            var gesture = command.Gestures[0];
            Press(editor, gesture);
            editor.Text.ReplaceLineEndings("\n").Should().Be("second\n");
            editor.Undo();
            editor.Text.ReplaceLineEndings("\n").Should().Be("first\nsecond\n");

            command.Gestures.Clear();
            command.Gestures.Add(new KeyGesture(Key.F12));
            Press(editor, gesture);
            editor.Text.ReplaceLineEndings("\n").Should().Be("first\nsecond\n");
            editor.CanRedo.Should().BeTrue("the old gesture must not reach the library's redo");
            Press(editor, ApplicationCommands.RedoCommand.Gestures[0]);
            editor.Text.ReplaceLineEndings("\n").Should().Be("second\n");
            editor.Undo();
            editor.Select(0, 0);
            Press(editor, command.Gestures[0]);
            editor.Text.ReplaceLineEndings("\n").Should().Be("second\n");
        }
        finally
        {
            command.Gestures.Clear();
            foreach (var gesture in originalGestures)
                command.Gestures.Add(gesture);
            window.Close();
        }
    }

    private static void Press(TextEditor editor, KeyGesture gesture)
    {
        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers,
        });
        Dispatcher.UIThread.RunJobs();
    }
}
