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

    // Posted at Background, the lowest priority the read waits behind, ahead of the read: still caught.
    [AvaloniaFact]
    public async Task A_revert_posted_at_Background_is_caught()
    {
        var (window, box) = Shown((box, _) => box.IsCheckedChanged += (_, _) =>
        {
            if (box.IsChecked == false)
                Dispatcher.UIThread.Post(() => box.IsChecked = true, DispatcherPriority.Background);
        });
        try
        {
            var outcome = await UiAutomationDriver.InteractAsync(box, "toggle", null);

            outcome.Detail.Should().Be("toggled 'Library', but its handler or a binding put it back: it is still on");
        }
        finally { window.Close(); }
    }

    // A radio button a click turned on and a posted refusal turned off again was put back; it is not one a click left on.
    [AvaloniaFact]
    public async Task A_radio_button_put_back_is_said_to_be_put_back()
    {
        var radio = new RadioButton { Name = "Public", Content = "Public", IsChecked = false };
        var window = new Window { Content = radio, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        radio.IsCheckedChanged += (_, _) =>
        {
            if (radio.IsChecked == true)
                Dispatcher.UIThread.Post(() => radio.IsChecked = false);
        };
        try
        {
            var outcome = await UiAutomationDriver.InteractAsync(radio, "toggle", null);

            outcome.Detail.Should().Be("toggled 'Public', but its handler or a binding put it back: it is still off");
        }
        finally { window.Close(); }
    }

    // Named as it was when it was toggled, as invoke names its target (#578), though its caption follows its state.
    [AvaloniaFact]
    public async Task The_box_is_named_as_it_was_before_the_toggle()
    {
        var (window, box) = Shown((box, _) => box.IsCheckedChanged += (_, _) => box.Content = box.IsChecked == true ? "On" : "Off");
        box.Content = "On";
        try
        {
            var outcome = await UiAutomationDriver.InteractAsync(box, "toggle", null);

            outcome.Detail.Should().Be("toggled 'On': it is now off");
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

            outcome.Detail.Should().Be("toggled 'Library': it is now off; a dialog opened during this call, which modalDialog "
                                       + "names, and the state may change again once it is answered");
        }
        finally
        {
            asked?.Close();
            window.Close();
        }
    }
}
