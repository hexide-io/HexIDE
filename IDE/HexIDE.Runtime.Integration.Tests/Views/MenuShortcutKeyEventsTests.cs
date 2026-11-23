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

namespace HexIDE.Runtime.Integration.Tests.Views;

public class MenuShortcutKeyEventsTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new Exception(message);
    }

    // #5371, measured native and p-code, oracle PR #5375: full shortcut key-event order.
    [AvaloniaTheory]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    // #1344: a parent's Click can enable the item. Hidden-item KeyPress remains unchanged/unmeasured.
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, true)]
    public void Shortcut_delivers_keys_before_clicks(bool preview, bool disabled, bool enableInTop, bool hidden)
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
               Begin VB.Menu mnuTop
                  Caption = "Top"
                  Begin VB.Menu mnuItem
                     Caption = "Item"
                     Shortcut = SHORTCUT
                     Enabled = ENABLED
                     Visible = VISIBLE
                  End
               End
            End
            Attribute VB_Name = "Form1"
            Private Sub Form_KeyDown(KeyCode As Integer, Shift As Integer)
                Debug.Print "FD" & KeyCode & "/" & Shift
            End Sub
            Private Sub Text1_KeyDown(KeyCode As Integer, Shift As Integer)
                Debug.Print "D" & KeyCode & "/" & Shift
            End Sub
            Private Sub Form_KeyPress(KeyAscii As Integer)
                Debug.Print "FP" & KeyAscii
            End Sub
            Private Sub Text1_KeyPress(KeyAscii As Integer)
                Debug.Print "P" & KeyAscii
            End Sub
            Private Sub Form_KeyUp(KeyCode As Integer, Shift As Integer)
                Debug.Print "FU" & KeyCode & "/" & Shift
            End Sub
            Private Sub Text1_KeyUp(KeyCode As Integer, Shift As Integer)
                Debug.Print "U" & KeyCode & "/" & Shift
            End Sub
            Private Sub mnuTop_Click()
                Debug.Print "Top"
                ENABLEITEM
            End Sub
            Private Sub mnuItem_Click()
                Debug.Print "Item"
            End Sub

            """;
        source = source.Replace("PREVIEW", preview ? "-1" : "0")
            .Replace("SHORTCUT", disabled ? "^E" : "^Q").Replace("ENABLED", disabled ? "0" : "-1")
            .Replace("VISIBLE", hidden ? "0" : "-1")
            .Replace("ENABLEITEM", enableInTop ? "mnuItem.Enabled = True" : "");
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source.ReplaceLineEndings("\r\n"), new Sink())!;
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
            printed.Clear();
            var key = disabled ? Key.E : Key.Q;
            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.None, null);
            window.KeyPress(key, RawInputModifiers.Control, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.Control, PhysicalKey.None, null);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            var expected = new List<string>();
            void Pair(string formEvent, string controlEvent)
            {
                if (preview) expected.Add(formEvent);
                expected.Add(controlEvent);
            }
            Pair("FD17/2", "D17/2");
            var code = disabled ? 69 : 81;
            Pair($"FD{code}/2", $"D{code}/2");
            expected.Add("Top");
            if (!hidden && (!disabled || enableInTop)) expected.Add("Item");
            if (disabled && !enableInTop) Pair("FP5", "P5");
            Pair($"FU{code}/2", $"U{code}/2");
            Pair("FU17/0", "U17/0");
            printed.Should().Equal(expected);
            box.Text.Should().BeEmpty();
            faults.Should().BeEmpty();
        }
        finally
        {
            window?.Close();
            VBDebugConsole.Output -= printed.Add;
            VBWindowContext.Faulted -= Faulted;
        }
    }
}
