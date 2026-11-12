using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

internal static class TabStripForm
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) { }
    }

    internal static Window Open(bool cancel = false, bool mouse = false, bool multi = false, bool focus = false)
    {
        var source = $$"""
            VERSION 5.00
            Begin VB.Form Form1
               ClientHeight = 3000
               ClientWidth = 5000
               Begin MSComctlLib.TabStrip TabStrip1
                  Left = 120
                  Top = 120
                  Width = 4000
                  Height = 2000
               End
               Begin VB.TextBox Text1
                  Left = 120
                  Top = 2400
                  Width = 1000
                  Height = 300
               End
            End
            Attribute VB_Name = "Form1"
            Private Sub Form_Load()
                TabStrip1.Tabs(1).Caption = "&Alpha"
                TabStrip1.Tabs.Add , , "&Beta"
                TabStrip1.Tabs.Add , , "Gamma"
                TabStrip1.Tabs.Add , , "&Delta"
                {{(multi ? "TabStrip1.Style = 1\n    TabStrip1.MultiSelect = True" : "")}}
                Text1.Text = ""
            End Sub
            Private Sub TabStrip1_BeforeClick(Cancel As Integer)
                Text1.Text = Text1.Text & "Before" & TabStrip1.SelectedItem.Index
                Cancel = {{(cancel ? "1" : "0")}}
            End Sub
            {{(mouse ? """
            Private Sub TabStrip1_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Text1.Text = Text1.Text & "Down" & TabStrip1.SelectedItem.Index
            End Sub
            Private Sub TabStrip1_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Text1.Text = Text1.Text & "Up" & TabStrip1.SelectedItem.Index & "(" & Button & "," & Shift & "," & X & "," & Y & ")"
            End Sub
            Private Sub Form_MouseDown(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Text1.Text = Text1.Text & "FormDown"
            End Sub
            Private Sub Form_MouseUp(Button As Integer, Shift As Integer, X As Single, Y As Single)
                Text1.Text = Text1.Text & "FormUp"
            End Sub
            Private Sub Form_Click()
                Text1.Text = Text1.Text & "FormClick"
            End Sub
            """ : "")}}
            {{(focus ? """
            Private Sub TabStrip1_GotFocus()
                Text1.Text = Text1.Text & "Got"
            End Sub
            Private Sub TabStrip1_LostFocus()
                Text1.Text = Text1.Text & "Lost"
            End Sub
            """ : "")}}
            Private Sub TabStrip1_Click()
                Text1.Text = Text1.Text & "Click" & TabStrip1.SelectedItem.Index
            End Sub
            """;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new Sink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
