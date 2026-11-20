using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

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
            var host = new StandaloneStandardLib(window, HexIDE.IDE.Distribution.DisplayName);
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
    public async Task The_host_distinguishes_empty_text_and_preserves_other_formats()
    {
        var window = new Window();
        window.Show();
        try
        {
            var host = new StandaloneStandardLib(window, HexIDE.IDE.Distribution.DisplayName);
            await host.Clipboard.Clear();
            (await host.Clipboard.GetText()).Should().BeNull();
            await host.Clipboard.SetText("");
            (await host.Clipboard.GetText()).Should().Be("");
            var custom = DataFormat.CreateStringApplicationFormat("hexide-test");
            var data = new DataTransfer();
            var item = DataTransferItem.CreateText("old");
            item.Set(custom, "preserve");
            var binary = DataFormat.CreateBytesApplicationFormat("hexide-binary");
            item.Set(binary, new byte[] { 0, 127, 255 });
            data.Add(item);
            await window.Clipboard!.SetDataAsync(data);
            await host.Clipboard.SetText("new");
            (await host.Clipboard.GetText()).Should().Be("new");
            (await window.Clipboard!.TryGetValueAsync(custom)).Should().Be("preserve");
            (await window.Clipboard!.TryGetValueAsync(binary)).Should().Equal(0, 127, 255);
            await host.Clipboard.SetText("");
            (await host.Clipboard.GetText()).Should().Be("");
            (await window.Clipboard!.TryGetValueAsync(custom)).Should().Be("preserve");
            await host.Clipboard.Clear();
            (await window.Clipboard!.GetDataFormatsAsync()).Should().BeEmpty();
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }

    // Host wiring for #4955's measured RTF rows and #5078's Windows companion formats.
    [AvaloniaFact]
    public async Task Rtf_round_trips_as_ansi_and_preserves_text_and_other_formats()
    {
        var window = new Window();
        window.Show();
        try
        {
            var host = new StandaloneStandardLib(window, HexIDE.IDE.Distribution.DisplayName);
            var clipboard = host.Clipboard;
            await clipboard.Clear();
            (await clipboard.HasOtherFormats()).Should().BeFalse();
            (await clipboard.GetRtf()).Should().BeNull();
            await clipboard.SetRtf("");
            (await clipboard.GetRtf()).Should().Be("");
            var rtf = DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsWindows() ? "Rich Text Format"
                : OperatingSystem.IsMacOS() ? "public.rtf" : "text/rtf");
            (await window.Clipboard!.TryGetValueAsync(rtf)).Should().Equal(0);
            await clipboard.SetRtf("{\\rtf1 €}");
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(Vb6TextEncoding.Ansi.CodePage)!;
            (await window.Clipboard!.TryGetValueAsync(rtf)).Should().Equal(encoding.GetBytes("{\\rtf1 €}\0"));
            (await clipboard.GetRtf()).Should().Be("{\\rtf1 €}");
            await clipboard.SetText("new");
            (await clipboard.GetRtf()).Should().Be("{\\rtf1 €}");
            await clipboard.SetRtf("{\\rtf1 replaced}");
            (await clipboard.GetText()).Should().Be("new");
            (await clipboard.HasOtherFormats()).Should().BeFalse();
            var custom = DataFormat.CreateStringApplicationFormat("hexide-rtf-test");
            var data = new DataTransfer();
            var item = DataTransferItem.CreateText("new");
            item.Set(rtf, encoding.GetBytes("{\\rtf1 replaced}\0"));
            item.Set(custom, "preserve");
            data.Add(item);
            await window.Clipboard!.SetDataAsync(data);
            (await clipboard.HasOtherFormats()).Should().BeTrue();
            await clipboard.SetRtf("{\\rtf1 last}");
            (await window.Clipboard!.TryGetValueAsync(custom)).Should().Be("preserve");
            (await clipboard.GetText()).Should().Be("new");
            await clipboard.Clear();
            (await clipboard.GetRtf()).Should().BeNull();
            (await clipboard.HasOtherFormats()).Should().BeFalse();
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Replacing_text_discards_windows_companions_but_replacing_rtf_keeps_them()
    {
        var window = new Window();
        window.Show();
        try
        {
            var host = new StandaloneStandardLib(window, HexIDE.IDE.Distribution.DisplayName);
            var companions = new[] { 1, 7, 16 }
                .Select(id => DataFormat.CreateBytesPlatformFormat($"Unknown_Format_{id}")).ToArray();
            var data = new DataTransfer();
            var item = DataTransferItem.CreateText("old");
            foreach (var format in companions) item.Set(format, new byte[] { 111, 108, 100, 0 });
            data.Add(item);
            await window.Clipboard!.SetDataAsync(data);
            // Assert the stale-byte regression independently before checking the new host members.
            await host.Clipboard.SetText("new");
            var formats = await window.Clipboard!.GetDataFormatsAsync();
            foreach (var companion in companions) formats.Should().NotContain(companion);
            (await host.Clipboard.GetText()).Should().Be("new");
            await window.Clipboard!.SetDataAsync(data);
            (await host.Clipboard.HasOtherFormats()).Should().BeFalse();
            await host.Clipboard.SetRtf("{\\rtf1 x}");
            foreach (var format in companions)
                (await window.Clipboard!.TryGetValueAsync(format)).Should().Equal(111, 108, 100, 0);
            (await host.Clipboard.HasOtherFormats()).Should().BeFalse();
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }
}
