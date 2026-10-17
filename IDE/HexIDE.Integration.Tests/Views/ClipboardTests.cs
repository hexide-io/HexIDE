using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class ClipboardTests
{
    [AvaloniaFact]
    public async Task Notepad_edit_handlers_copy_cut_and_paste_through_the_window_clipboard()
    {
        var editor = new VBTextBox { Text = "alpha beta" };
        var window = new Window { Content = editor };
        window.Show();
        try
        {
            var host = new StandaloneStandardLib(window);
            await host.Clipboard.Clear();
            var context = new ModuleExecutionContext();
            var env = new ExecutionEnvironment();
            env.DefineVariable("Text1", context.State.Alloc(new Vb6Value(editor)));
            var vb = new BasicInterpreter(host, context, env, """
                Sub CopyText()
                    Clipboard.Clear
                    Clipboard.SetText Text1.SelText
                End Sub
                Sub CutText()
                    With Text1
                        Clipboard.Clear
                        Clipboard.SetText .SelText
                        .SelText = ""
                    End With
                End Sub
                Sub PasteText()
                    If Clipboard.GetFormat(vbCFText) Then Text1.SelText = Clipboard.GetText(vbCFText)
                End Sub
                Text1.SelStart = 0
                Text1.SelLength = 5
                """);
            await vb.Execute();
            await vb.ExecuteSub("CopyText");
            (await window.Clipboard!.TryGetTextAsync()).Should().Be("alpha");
            editor.Text.Should().Be("alpha beta");
            await vb.ExecuteSub("CutText");
            editor.Text.Should().Be(" beta");
            await vb.ExecuteSub("PasteText");
            editor.Text.Should().Be("alpha beta");
            await window.Clipboard!.SetTextAsync("external");
            editor.SelectionStart = editor.SelectionEnd = editor.Text!.Length;
            await vb.ExecuteSub("PasteText");
            editor.Text.Should().Be("alpha betaexternal");
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task The_host_distinguishes_empty_text_and_refuses_to_erase_other_formats()
    {
        var window = new Window();
        window.Show();
        try
        {
            var host = new StandaloneStandardLib(window);
            await host.Clipboard.Clear();
            (await host.Clipboard.GetText()).Should().BeNull();
            await host.Clipboard.SetText("");
            (await host.Clipboard.GetText()).Should().Be("");
            var custom = DataFormat.CreateStringApplicationFormat("hexide-test");
            await window.Clipboard!.SetValueAsync(custom, "preserve");
            var write = () => host.Clipboard.SetText("new");
            await write.Should().ThrowAsync<NotImplementedException>();
            (await window.Clipboard!.TryGetValueAsync(custom)).Should().Be("preserve");
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }
}
