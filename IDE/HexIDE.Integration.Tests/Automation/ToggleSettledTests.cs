using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A toggle is replied to with the state the box ends in, not the one it held a moment after the toggle (#2050). A
/// refusal that puts a box back from a posted callback, as the Components dialog does for a library a form uses, was
/// reported as done.
/// </summary>
public class ToggleSettledTests
{
    private static (Window Window, CheckBox Box) Shown(System.Action<CheckBox, Window> refuse)
    {
        var box = new CheckBox { Name = "Library", Content = "Library", IsChecked = true };
        var window = new Window { Content = box, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        refuse(box, window);
        return (window, box);
    }

    [AvaloniaFact]
    public async Task An_uncheck_put_back_by_posted_work_is_reported_as_put_back()
    {
        var (window, box) = Shown((box, _) => box.IsCheckedChanged += (_, _) =>
        {
            if (box.IsChecked == false)
                Dispatcher.UIThread.Post(() => box.IsChecked = true);
        });
        try
        {
            var outcome = await UiAutomationDriver.InteractAsync(box, "toggle", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("toggled 'Library', but its handler or a binding put it back: it is still on");
            box.IsChecked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task An_uncheck_that_holds_is_reported_as_done()
    {
        var (window, box) = Shown((_, _) => { });
        try
        {
            var outcome = await UiAutomationDriver.InteractAsync(box, "toggle", null);

            outcome.Detail.Should().Be("toggled 'Library': it is now off");
        }
        finally { window.Close(); }
    }

    // The Components dialog asks first, and puts the box back once its message box is answered: no read before that can
    // know, so the reply says the state may still change (#2050).
    [AvaloniaFact]
    public async Task A_toggle_that_opened_a_dialog_says_the_state_may_still_change()
    {
        Window? asked = null;
        var (window, box) = Shown((box, owner) => box.IsCheckedChanged += (_, _) =>
        {
            if (box.IsChecked == false && asked is null)
            {
                asked = new Window { Title = "Remove?" };
                _ = asked.ShowDialog(owner);
            }
        });
        try
        {
            var outcome = await UiAutomationDriver.InteractAsync(box, "toggle", null);

            outcome.Detail.Should().Be("toggled 'Library': it is now off; a dialog opened during this call, and the state may "
                                       + "change again once it is answered");
        }
        finally
        {
            asked?.Close();
            window.Close();
        }
    }
}
