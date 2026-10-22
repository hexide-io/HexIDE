using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// interact's close closes a window as a person does with its close box (#829). Nothing on the surface could: the title
/// bar is the platform's, outside the control tree, the platform takes Alt+F4 before a key reaches a control, and a
/// programmatic close is the one a guard lets through, so a MsgBox that refuses a person's close could not be shown to.
/// </summary>
public class CloseWindowTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static InteractOutcome Close(Control target)
    {
        var outcome = UiAutomationDriver.Interact(target, "close", null);
        Dispatcher.UIThread.RunJobs();
        return UiAutomationDriver.AfterClose(target, outcome);
    }

    [AvaloniaFact]
    public void A_window_closes_as_its_close_box_closes_it()
    {
        var button = new Button { Content = "OK" };
        var window = new Window { Title = "Dialog", Width = 200, Height = 100, Content = button };
        var seen = new List<(WindowCloseReason, bool)>();
        window.Closing += (_, e) => seen.Add((e.CloseReason, e.IsProgrammatic));
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var outcome = Close(button);

        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("asked 'Dialog' to close, as its close box does; it is closed");
        seen.Should().Equal([(WindowCloseReason.WindowClosing, false)], "a person's close, not one from code");
        window.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_window_that_refuses_a_person_s_close_stays_open_and_the_reply_says_so()
    {
        var window = new Window { Title = "Busy", Width = 200, Height = 100 };
        window.Closing += (_, e) => e.Cancel = !e.IsProgrammatic;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = Close(window);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().EndWith("; it is still open: the window, or the program behind it, refused, as it may a person's close");
            window.IsVisible.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_window_under_a_modal_dialog_is_refused_and_the_dialog_can_be_closed()
    {
        var owner = new Window { Title = "Owner", Width = 300, Height = 200 };
        owner.Show();
        var dialog = new Window { Title = "Settings", Width = 200, Height = 100 };
        var shown = dialog.ShowDialog(owner);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var refused = Close(owner);

            refused.Success.Should().BeFalse();
            refused.Error.Should().Contain("'Settings'").And.Contain("nothing was done");
            (owner.IsVisible, dialog.IsVisible).Should().Be((true, true), "a person's close box does nothing there");

            Close(dialog).Detail.Should().EndWith("; it is closed");
            shown.IsCompleted.Should().BeTrue("the dialog's caller hears it closed");
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            owner.Close();
        }
    }

    private static Window RunForm(string code)
    {
        var frm = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   3000\r\n"
                  + "   ClientWidth     =   4000\r\n   Begin VB.CommandButton Command1 \r\n      Caption         =   \"Ask\"\r\n"
                  + "      Height          =   375\r\n      Left            =   120\r\n      Top             =   120\r\n"
                  + "      Width           =   1215\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n" + code.ReplaceLineEndings("\r\n");
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, frm, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window);
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaTheory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void A_running_form_s_QueryUnload_hears_the_close_box_and_may_keep_it_open(int cancel, bool stays)
    {
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        var window = RunForm($"""
            Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
                Debug.Print "QueryUnload " & UnloadMode
                Cancel = {cancel}
            End Sub
            """);
        try
        {
            var outcome = Close(window);

            printed.Should().Equal(["QueryUnload 0"], "UnloadMode 0 is vbFormControlMenu, the close box's");
            outcome.Detail.Should().EndWith(stays ? "it is still open: the window, or the program behind it, refused, as it may a person's close" : "; it is closed");
            window.IsVisible.Should().Be(stays);
        }
        finally
        {
            VBDebugConsole.Output -= print;
            if (window.IsVisible) window.Close();
        }
    }

    [AvaloniaFact]
    public void A_Yes_No_MsgBox_refuses_the_close_box_as_VB6_s_does()
    {
        var boxes = new List<Window>();
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (w.Content is HexIDE.Runtime.Dialogs.MessageBox)
                boxes.Add(w);
        });
        var window = RunForm("""
            Private Sub Command1_Click()
                MsgBox "Sure?", vbYesNo
            End Sub
            """);
        try
        {
            window.GetLogicalDescendants().OfType<VBCommandButton>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            boxes.Should().ContainSingle("the premise: the click is waiting on its box");

            var outcome = Close(boxes[0]);

            outcome.Detail.Should().EndWith("still open: the window, or the program behind it, refused, as it may a person's close");
            boxes[0].IsVisible.Should().BeTrue();
        }
        finally
        {
            foreach (var box in boxes.Where(b => b.IsVisible)) box.Close();
            if (window.IsVisible) window.Close();
        }
    }
}
