using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Keys a TextBox takes itself, after a selection code set (#1385). Every row was measured on vb6.exe with real keys,
/// native and p-code agreeing (the oracle's "Keys after a selection set from code"). A selection set from code left
/// Avalonia's caret where its keys did not expect it, so Ctrl+End with the selection running to the end of the text went
/// to 0. In the Notepad demo, Select All, Ctrl+End and Ctrl+V pasted at the start, and Find Up searched from the middle.
/// </summary>
public class TextBoxSelectionAfterInputTests
{
    private const string Code = """
        Sub SelectAll()
            Text1.SelStart = 0
            Text1.SelLength = Len(Text1.Text)
        End Sub
        Sub CopyText()
            Clipboard.Clear
            Clipboard.SetText Text1.SelText
        End Sub
        Sub PasteText()
            If Clipboard.GetFormat(vbCFText) Then Text1.SelText = Clipboard.GetText(vbCFText)
        End Sub
        """;

    private sealed class Box : System.IDisposable
    {
        public required Window Window { get; init; }
        public required VBTextBox Editor { get; init; }
        public required BasicInterpreter Vb { get; init; }

        // What code reads, as SelStart and SelLength.
        public string Selection =>
            $"{(TextBoxMembers.TryGet(Editor, "SelStart", out var start) ? start.Value : null)} "
            + $"{(TextBoxMembers.TryGet(Editor, "SelLength", out var length) ? length.Value : null)}";

        public void Press(KeyGesture gesture, PhysicalKey physical)
        {
            Window.KeyPress(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, physical, null);
            Window.KeyRelease(gesture.Key, (RawInputModifiers)gesture.KeyModifiers, physical, null);
            Dispatcher.UIThread.RunJobs();
        }

        public void Type(string text)
        {
            Window.KeyTextInput(text);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose() => Window.Close();
    }

    // The platform's own gestures: Ctrl+End is not what every platform binds.
    private static Avalonia.Input.Platform.PlatformHotkeyConfiguration Keys =>
        Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration;

    private static async Task<Box> Open(bool multiLine, string text)
    {
        var editor = new VBTextBox();
        editor.ApplyLayout(multiLine, multiLine ? 2 : 0);
        var window = new Window { Content = editor, Width = 300, Height = 100 };
        window.Show();
        // Avalonia finds the end of a multi-line text from its layout.
        window.UpdateLayout();
        var host = new StandaloneStandardLib(window);
        var context = new ModuleExecutionContext();
        var env = new ExecutionEnvironment();
        env.DefineVariable("Text1", context.State.Alloc(new Vb6Value(editor)));
        var vb = new BasicInterpreter(host, context, env, Code + "\nText1.Text = \"" + text + "\"\n");
        await vb.Execute();
        editor.Focus();
        Dispatcher.UIThread.RunJobs();
        return new Box { Window = window, Editor = editor, Vb = vb };
    }

    // SelStart and SelLength set from code, then a key: the caret goes where the key says, with nothing selected.
    [AvaloniaTheory]
    [InlineData(false, 0, 6, "end", "6 0")]
    [InlineData(true, 0, 6, "end", "6 0")]
    [InlineData(false, 2, 0, "end", "6 0")]
    [InlineData(false, 2, 2, "home", "0 0")]
    [InlineData(true, 2, 2, "home", "0 0")]
    [InlineData(false, 0, 6, "home", "0 0")]
    public async Task After_a_selection_from_code_Ctrl_End_and_Ctrl_Home_go_to_the_end_and_the_start(
        bool multiLine, int start, int length, string key, string expected)
    {
        using var box = await Open(multiLine, "abcdef");
        TextBoxMembers.TrySet(box.Editor, "SelStart", new Vb6Value(start)).Should().BeTrue();
        TextBoxMembers.TrySet(box.Editor, "SelLength", new Vb6Value(length)).Should().BeTrue();
        box.Selection.Should().Be($"{start} {length}");

        if (key == "end")
            box.Press(Keys.MoveCursorToTheEndOfDocument.First(), PhysicalKey.End);
        else
            box.Press(Keys.MoveCursorToTheStartOfDocument.First(), PhysicalKey.Home);

        box.Selection.Should().Be(expected);
    }

    // The running TextBox key sequences measured in #2423.
    [AvaloniaTheory]
    [InlineData(Key.End, PhysicalKey.End, Key.Back, PhysicalKey.Backspace, "3 0", "2 0")]
    [InlineData(Key.Home, PhysicalKey.Home, Key.Delete, PhysicalKey.Delete, "0 0", "0 0")]
    public async Task Select_all_then_End_or_Home_collapses_the_selection_before_deleting(
        Key move, PhysicalKey physicalMove, Key delete, PhysicalKey physicalDelete,
        string movedSelection, string finalSelection)
    {
        using var box = await Open(false, "");
        box.Type("kkk");
        box.Press(Keys.SelectAll.First(), PhysicalKey.A);
        box.Selection.Should().Be("0 3");

        box.Press(new KeyGesture(move), physicalMove);

        box.Selection.Should().Be(movedSelection);
        box.Press(new KeyGesture(delete), physicalDelete);
        box.Editor.Text.Should().Be("kk");
        box.Selection.Should().Be(finalSelection);
    }

    [AvaloniaFact]
    public async Task Typing_replaces_a_selection_from_code_and_leaves_the_caret_after_it()
    {
        using var box = await Open(false, "abcdef");
        TextBoxMembers.TrySet(box.Editor, "SelStart", new Vb6Value(2));
        TextBoxMembers.TrySet(box.Editor, "SelLength", new Vb6Value(2));

        box.Type("X");

        box.Editor.Text.Should().Be("abXef");
        box.Selection.Should().Be("3 0");
    }

    // The Notepad's own steps: two lines typed, Select All and Copy from its menu, Ctrl+End, then a paste, by the box
    // itself (the menu's Paste was disabled) or by the menu's code. A character typed after Ctrl+End shows where it went.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Select_All_then_Ctrl_End_and_a_paste_appends_and_SelStart_is_the_end(bool ownPaste)
    {
        using var box = await Open(true, "");
        await box.Window.Clipboard!.ClearAsync();
        try
        {
            box.Type("ab");
            box.Press(new KeyGesture(Key.Enter), PhysicalKey.Enter);
            box.Type("cd");
            await box.Vb.ExecuteSub("SelectAll");
            await box.Vb.ExecuteSub("CopyText");

            box.Press(Keys.MoveCursorToTheEndOfDocument.First(), PhysicalKey.End);
            box.Type("e");
            if (ownPaste)
            {
                box.Press(Keys.Paste.First(), PhysicalKey.V);
                // The box's own paste reads the clipboard asynchronously.
                for (var i = 0; i < 50 && box.Editor.Text!.Length < 13; i++)
                {
                    await Task.Delay(10);
                    Dispatcher.UIThread.RunJobs();
                }
            }
            else
                await box.Vb.ExecuteSub("PasteText");

            box.Editor.Text!.ReplaceLineEndings("|").Should().Be("ab|cdeab|cd");
            box.Selection.Should().Be("13 0");
        }
        finally
        {
            await box.Window.Clipboard!.ClearAsync();
        }
    }
}
