using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class TextBoxKeyPressTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new Exception(message);
    }

    // Unrecorded KeyCode mappings retain their existing Down/Up behavior; KeyAscii is the measured character.
    [AvaloniaTheory]
    [InlineData(false, "k", "k", "D75,P107,C,U75")]
    [InlineData(false, "K", "K", "D75,P75,C,U75")]
    [InlineData(false, "a", "b", "D65,P97,C,U65")]
    [InlineData(false, "q", "", "P113")]
    [InlineData(false, "w", "w", "P119,C")]
    [InlineData(true, "k", "k", "FD75,D75,FP107,P107,C,FU75,U75")]
    [InlineData(true, "x", "x", "FD88,FP120,P120,C,FU88,U88")]
    [InlineData(true, "z", "", "FP122")]
    [InlineData(true, "e", "E", "FP101,P69,C")]
    [InlineData(false, "Back", "", "D8,P8,C,U8")]
    [InlineData(true, "Back", "", "FD8,D8,FP8,P8,C,FU8,U8")]
    [InlineData(false, "Delete", "", "D46,U46")]
    [InlineData(false, "Left", "", "D37,U37")]
    [InlineData(false, "F2", "", "D113,U113")]
    [InlineData(false, "AltX", "", "D88,U88")]
    [InlineData(false, "CtrlA", "", "D65,P1,U65")]
    public void Real_handlers_filter_typing_and_preview_interleaves(bool preview, string input, string expected, string events)
        => Run(preview, input, expected, events);

    [AvaloniaFact]
    public void KeyPreview_can_be_set_and_read_by_the_running_program() =>
        Run(true, "k", "k", "FD75,D75,FP107,P107,C,FU75,U75", runtimePreview: true);

    [AvaloniaFact]
    public void KeyPress_keeps_the_control_array_Index_before_KeyAscii() =>
        Run(false, "a", "b", "D65,P97,C,U65", array: true);

    [AvaloniaFact]
    public void Unmeasured_non_ASCII_replacement_is_refused() =>
        Run(false, "a", "", "D65,P97,U65", unsupported: true);

    private static void Run(bool preview, string input, string expected, string events,
        bool runtimePreview = false, bool array = false, bool unsupported = false)
    {
        var source = """
            VERSION 5.00
            Begin VB.Form Form1
               ClientHeight = 1500
               ClientWidth = 3000
               KeyPreview = PREVIEW
               Begin VB.TextBox Text1
                  Height = 315
                  Width = 1800
                  Text = ""
               End
            End
            Attribute VB_Name = "Form1"
            Private Sub Form_KeyDown(KeyCode As Integer, Shift As Integer)
                Debug.Print "FD" & KeyCode
                If KeyCode = 88 Then KeyCode = 0
            End Sub
            Private Sub Form_KeyPress(KeyAscii As Integer)
                Debug.Print "FP" & KeyAscii
                If KeyAscii = 122 Then KeyAscii = 0
                If KeyAscii = 101 Then KeyAscii = 69
            End Sub
            Private Sub Form_KeyUp(KeyCode As Integer, Shift As Integer)
                Debug.Print "FU" & KeyCode
            End Sub
            Private Sub Text1_KeyDown(KeyCode As Integer, Shift As Integer)
                Debug.Print "D" & KeyCode
                If KeyCode = 75 Then KeyCode = 0
            End Sub
            Private Sub Text1_KeyPress(KeyAscii As Integer)
                Debug.Print "P" & KeyAscii
                If KeyAscii = 97 Then KeyAscii = 98
                If KeyAscii = 113 Then KeyAscii = 0
            End Sub
            Private Sub Text1_Change()
                Debug.Print "C"
            End Sub
            Private Sub Text1_KeyUp(KeyCode As Integer, Shift As Integer)
                Debug.Print "U" & KeyCode
            End Sub

            """;
        if (unsupported)
            source = source.Replace("KeyAscii = 98", "KeyAscii = 128");
        if (runtimePreview)
            source += "Private Sub Form_Load()\r\nKeyPreview = True\r\nIf Not KeyPreview Then Err.Raise 5\r\nEnd Sub\r\n";
        if (array)
        {
            source = source.Replace("Begin VB.TextBox Text1", "Begin VB.TextBox Text1\r\nIndex = 3")
                .Replace("Text1_KeyDown(", "Text1_KeyDown(Index As Integer, ")
                .Replace("Text1_KeyPress(", "Text1_KeyPress(Index As Integer, ")
                .Replace("Text1_KeyUp(", "Text1_KeyUp(Index As Integer, ")
                .Replace("Text1_Change()", "Text1_Change(Index As Integer)")
                .Replace("Debug.Print \"P\" & KeyAscii", "If Index <> 3 Then Err.Raise 5\r\nDebug.Print \"P\" & KeyAscii");
        }
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            source.Replace("PREVIEW", preview && !runtimePreview ? "-1" : "0").ReplaceLineEndings("\r\n"), new Sink())!;
        project.AddForm(form);
        var printed = new List<string>();
        var faults = new List<Exception>();
        void Faulted(VBWindowContext _, Exception e) => faults.Add(e);
        VBWindowContext.Faulted += Faulted;
        VBDebugConsole.Output += printed.Add;
        VBFormRuntime? window = null;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out window, null);
            Dispatcher.UIThread.RunJobs();
            var box = window.GetVisualDescendants().OfType<VBTextBox>().Single();
            box.Focus();
            window.UpdateLayout();
            if (input == "Back")
            {
                box.Text = "k";
                box.CaretIndex = 1;
            }
            printed.Clear();
            var key = input switch
            {
                "AltX" => Key.X, "CtrlA" => Key.A,
                _ => Enum.Parse<Key>(input, true),
            };
            var modifiers = input switch
            {
                "K" => KeyModifiers.Shift, "AltX" => KeyModifiers.Alt,
                "CtrlA" => KeyModifiers.Control, _ => KeyModifiers.None,
            };
            window.KeyPress(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
            if (input.Length == 1)
                window.KeyTextInput(input);
            window.KeyRelease(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            if (unsupported)
                faults.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>();
            else
                faults.Should().BeEmpty();
            box.Text.Should().Be(expected);
            printed.Should().Equal(events.Split(','));
        }
        finally
        {
            window?.Close();
            VBDebugConsole.Output -= printed.Add;
            VBWindowContext.Faulted -= Faulted;
        }
    }
}
