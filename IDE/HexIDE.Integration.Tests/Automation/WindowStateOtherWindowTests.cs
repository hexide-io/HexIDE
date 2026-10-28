using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// set_window_state reaches a window other than the IDE's own, such as a running form, and changes it as a person does
/// with its caption buttons and frame, so what a person couldn't do is refused (#2357).
/// </summary>
public class WindowStateOtherWindowTests
{
    private static Window Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void A_window_without_a_minimize_or_maximize_box_is_refused_those_states()
    {
        var window = Show(new Window { Title = "Fixed", Width = 200, Height = 100, CanMinimize = false, CanMaximize = false });
        try
        {
            HexIdeTools.WindowBoxRefusal(window, "Form1", WindowState.Minimized, resizes: false).Should().Be(
                "'Form1' has no minimize box, so a person can't minimize it. On a running form, MinButton, ControlBox and "
                + "BorderStyle decide whether it has one; the program can still set its WindowState. Nothing was changed.");
            HexIdeTools.WindowBoxRefusal(window, "Form1", WindowState.Maximized, resizes: false).Should().Be(
                "'Form1' has no maximize box, so a person can't maximize it. On a running form, MaxButton, ControlBox and "
                + "BorderStyle decide whether it has one; the program can still set its WindowState. Nothing was changed.");
            HexIdeTools.WindowBoxRefusal(window, "Form1", WindowState.Normal, resizes: true).Should().BeNull(
                "a restore needs no box, and the window can still be resized");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_fixed_border_is_refused_a_size_and_not_a_move()
    {
        var window = Show(new Window { Title = "Fixed", Width = 200, Height = 100, CanResize = false });
        try
        {
            HexIdeTools.WindowBoxRefusal(window, "Form1", WindowState.Normal, resizes: true).Should().Be(
                "'Form1' has a fixed border, so a person can't resize it; width and height are refused. x and y alone move "
                + "it. Nothing was changed.");
            HexIdeTools.WindowBoxRefusal(window, "Form1", WindowState.Normal, resizes: false).Should().BeNull();
            HexIdeTools.WindowBoxRefusal(window, "Form1", WindowState.Minimized, resizes: false).Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_window_under_a_modal_dialog_or_disabled_is_refused()
    {
        var owner = Show(new Window { Title = "Form1", Width = 300, Height = 200 });
        var dialog = new Window { Title = "Dialog", Width = 100, Height = 50 };
        _ = dialog.ShowDialog(owner);
        Dispatcher.UIThread.RunJobs();
        try
        {
            UiAutomationDriver.WindowRefusal(owner, "set_window_state", "Form1").Should()
                .StartWith("'Form1' is in a window a modal dialog has disabled: 'Dialog'")
                .And.Contain("'set_window_state' was refused and nothing was done");
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.WindowRefusal(owner, "set_window_state").Should().BeNull();

            owner.IsEnabled = false;
            UiAutomationDriver.WindowRefusal(owner, "set_window_state", "Form1").Should()
                .StartWith("'Form1' is disabled (\"isEnabled\": false).")
                .And.Contain("'set_window_state' was refused and nothing was done");
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [Fact]
    public void The_program_wait_reads_as_a_sentence_of_the_note()
    {
        HexIdeTools.ProgramNote(" (the program opened 'Form2')").Should().Be("The program opened 'Form2'.");
        HexIdeTools.ProgramNote(" (the program is paused at Form1 line 12 (breakpoint), with Form_Resize not finished)")
            .Should().Be("The program is paused at Form1 line 12 (breakpoint), with Form_Resize not finished.");
        HexIdeTools.ProgramNote("  ").Should().BeEmpty();
    }
}
