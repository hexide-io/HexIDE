using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 native and p-code measurements in https://github.com/hexide-io/HexIDE/issues/5300 and oracle PR #5309.
public class TextBoxLimitsTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    [AvaloniaTheory]
    [InlineData("", "0 False")]
    [InlineData("Text1.MaxLength = 5 : Text1.Locked = True", "5 True")]
    [InlineData("Text1.Locked = 5", "0 True")]
    public void Defaults_and_assignments(string code, string expected)
    {
        using var probe = new Probe(code + "\nDebug.Print CStr(Text1.MaxLength) & \" \" & CStr(Text1.Locked)\n"
            + "Debug.Print TypeName(Text1.MaxLength) & \" \" & TypeName(Text1.Locked)");
        probe.Printed.Should().Equal(expected, "Long Boolean");
    }

    [AvaloniaTheory]
    [InlineData("-1", 380, 5)]
    [InlineData("65536", 380, 5)]
    [InlineData("70000", 380, 5)]
    [InlineData("2147483647", 380, 5)]
    [InlineData("True", 380, 5)]
    [InlineData("32767", 0, 32767)]
    [InlineData("32768", 0, 32768)]
    [InlineData("65535", 0, 65535)]
    [InlineData("2.6", 0, 3)]
    [InlineData("1.5", 0, 2)]
    [InlineData("2.5", 0, 2)]
    [InlineData("-0.4", 0, 0)]
    [InlineData("\"3\"", 0, 3)]
    [InlineData("\"x\"", 13, 5)]
    public void MaxLength_rounds_and_checks_the_range(string value, int error, int limit)
    {
        using var probe = new Probe($"""
            Text1.MaxLength = 5
            On Error Resume Next
            Text1.MaxLength = {value}
            Debug.Print CStr(Err.Number) & " " & CStr(Text1.MaxLength)
            """);
        probe.Printed.Should().Equal($"{error} {limit}");
    }

    [AvaloniaTheory]
    [InlineData("", 5, false, 0, 0, "Text1.Text = \"abcdefgh\"", "abcde", 1)]
    [InlineData("abcdefgh", 0, false, 8, 0, "Text1.MaxLength = 3", "abcdefgh", 0)]
    [InlineData("abc", 5, false, 3, 0, "Text1.SelText = \"defgh\"", "abcde", 1)]
    [InlineData("abc", 0, true, 3, 0, "Text1.SelText = \"Q\"", "abcQ", 1)]
    [InlineData("abc", 0, true, 3, 0, "Text1.Text = \"zz\"", "zz", 1)]
    public void Code_edits(string before, int limit, bool locked, int start, int length, string edit,
        string after, int changes)
    {
        using var probe = new Probe(Setup(before, limit, locked, start, length) + "\n" + edit);
        probe.Check(after, changes);
    }

    [AvaloniaTheory]
    [InlineData("", 5, false, 0, 0, "abcdefgh", "abcde", 5)]
    [InlineData("abcdefgh", 5, false, 8, 0, "x", "abcdefgh", 0)]
    [InlineData("abc", 5, false, 1, 1, "xyz", "axyzc", 3)]
    [InlineData("", 0, false, 0, 0, "abcdefghij", "abcdefghij", 10)]
    [InlineData("", 65535, false, 0, 0, "abc", "abc", 3)]
    [InlineData("abc", 0, true, 3, 0, "xyz", "abc", 0)]
    public void Typing(string before, int limit, bool locked, int start, int length, string input,
        string after, int changes)
    {
        using var probe = new Probe(Setup(before, limit, locked, start, length));
        foreach (var letter in input)
            probe.Press(new KeyGesture(Enum.Parse<Key>(letter.ToString(), true)), letter.ToString());
        probe.Check(after, changes);
    }

    [AvaloniaTheory]
    [InlineData(Key.Back)]
    [InlineData(Key.Delete)]
    public void Locked_blocks_deletion(Key key)
    {
        using var probe = new Probe(Setup("abc", 0, true, 3, 0));
        probe.Press(new KeyGesture(key));
        probe.Check("abc", 0);
    }

    [AvaloniaTheory]
    [InlineData("paste", false, "abPQR", 1, "PQRSTUV")]
    [InlineData("paste", true, "abc", 0, "PQRSTUV")]
    [InlineData("copy", true, "abc", 0, "abc")]
    [InlineData("cut", true, "abc", 0, "abc")]
    public async Task Clipboard_shortcuts(string action, bool locked, string after, int changes, string clipboard)
    {
        using var probe = new Probe(locked ? Setup("abc", 0, true, 0, 3) : Setup("ab", 5, false, 2, 0));
        var keys = Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration;
        var gesture = action switch { "copy" => keys.Copy[0], "cut" => keys.Cut[0], _ => keys.Paste[0] };
        var board = probe.Window.Clipboard!;
        await board.SetTextAsync("PQRSTUV");
        try
        {
            probe.Press(gesture);
            (await board.TryGetTextAsync()).Should().Be(clipboard);
            probe.Check(after, changes);
        }
        finally
        {
            await board.ClearAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_properties_load_without_cutting_text(bool unlock)
    {
        using var probe = new Probe($"""
            Debug.Print CStr(Text1.MaxLength) & " " & CStr(Text1.Locked) & " " & Text1.Text
            {(unlock ? "Text1.Locked = False" : "")}
            Text1.SelStart = Len(Text1.Text)
            ready = True
            """, """
            MaxLength = 4
            Locked = -1 'True
            Text = "abcdefg"
            """);
        probe.Printed.Should().Equal("4 True abcdefg");
        probe.Press(new KeyGesture(Key.X), "x");
        probe.Check("abcdefg", null);
    }

    private static string Setup(string text, int limit, bool locked, int start, int length) => $"""
        Text1.MaxLength = 0
        Text1.Text = "{text}"
        Text1.MaxLength = {limit}
        Text1.Locked = {locked}
        Text1.SelStart = {start}
        Text1.SelLength = {length}
        ready = True
        """;

    private sealed class Probe : IDisposable
    {
        public VBFormRuntime Window { get; }
        public List<string> Printed { get; } = [];
        private readonly List<Exception> faults = [];
        private readonly VBTextBox box;

        public Probe(string code, string saved = "")
        {
            var source = $$"""
                VERSION 5.00
                Begin VB.Form Form1
                   ClientHeight = 1500
                   ClientWidth = 3000
                   Begin VB.TextBox Text1
                      Height = 315
                      Width = 1800
                      {{saved}}
                   End
                End
                Attribute VB_Name = "Form1"
                Private ready As Boolean
                Private Sub Form_Load()
                    {{code}}
                End Sub
                Private Sub Text1_Change()
                    If ready Then Debug.Print "Change"
                End Sub

                """;
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new Sink())!;
            project.AddForm(form);
            VBDebugConsole.Output += Printed.Add;
            VBWindowContext.CompileError += Faulted;
            VBWindowContext.RunTimeError += Faulted;
            VBWindowContext.Faulted += Faulted;
            try
            {
                VBLoader.RunForm(form, CancellationToken.None, out var window, null);
                Window = window;
                Dispatcher.UIThread.RunJobs();
                faults.Should().BeEmpty();
                box = Window.GetVisualDescendants().OfType<VBTextBox>().Single();
                box.Focus();
                Window.UpdateLayout();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Faulted(VBWindowContext _, Exception error) => faults.Add(error);

        public void Press(KeyGesture gesture, string? text = null)
        {
            var modifiers = (RawInputModifiers)gesture.KeyModifiers;
            Window.KeyPress(gesture.Key, modifiers, PhysicalKey.None, null);
            if (text is not null)
                Window.KeyTextInput(text);
            Window.KeyRelease(gesture.Key, modifiers, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
        }

        public void Check(string text, int? changes)
        {
            faults.Should().BeEmpty();
            box.Text.Should().Be(text);
            if (changes is { } count)
                Printed.Count(line => line == "Change").Should().Be(count);
        }

        public void Dispose()
        {
            Window?.Close();
            VBWindowContext.CompileError -= Faulted;
            VBWindowContext.RunTimeError -= Faulted;
            VBWindowContext.Faulted -= Faulted;
            VBDebugConsole.Output -= Printed.Add;
        }
    }
}
