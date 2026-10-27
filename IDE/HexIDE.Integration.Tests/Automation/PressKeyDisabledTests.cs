using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// press_key on a disabled control is refused, as interact's click and invoke are (#2366). The control couldn't take focus,
/// so the routed fallback pressed the key on it all the same, and the reply said it had.
/// </summary>
public class PressKeyDisabledTests
{
    private static (Window Window, ComboBox Disabled, ComboBox Enabled, StackPanel Off, TextBox InOff) Build()
    {
        var disabled = new ComboBox { Name = "Region", IsEnabled = false, ItemsSource = new[] { "A", "B" } };
        var enabled = new ComboBox { Name = "Language", ItemsSource = new[] { "A", "B" } };
        var inOff = new TextBox { Name = "Inner" };
        var off = new StackPanel { Name = "Off", IsEnabled = false, Children = { inOff } };
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { enabled, disabled, off } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, disabled, enabled, off, inOff);
    }

    [AvaloniaFact]
    public void A_key_on_a_disabled_control_is_refused_and_never_reaches_it()
    {
        var (window, disabled, _, _, _) = Build();
        var keys = 0;
        disabled.AddHandler(InputElement.KeyDownEvent, (_, _) => keys++, handledEventsToo: true);
        try
        {
            var outcome = UiAutomationDriver.PressKey(disabled, "Down", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("is disabled (\"isEnabled\": false). No person could use it, so 'press_key' was "
                + "refused and nothing was done. Wait until it is enabled before trying again");
            keys.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_key_on_a_disabled_container_is_refused_and_never_reaches_what_it_holds()
    {
        var (window, _, _, off, inOff) = Build();
        var keys = 0;
        inOff.AddHandler(InputElement.KeyDownEvent, (_, _) => keys++, handledEventsToo: true);
        try
        {
            var outcome = UiAutomationDriver.PressKey(off, "A", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("Its container 'StackPanel[Off]'").And.Contain("'press_key' was refused and nothing was done");
            keys.Should().Be(0);
            inOff.Text.Should().BeNullOrEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_key_on_an_enabled_control_is_still_pressed()
    {
        var (window, _, enabled, _, _) = Build();
        try
        {
            UiAutomationDriver.PressKey(enabled, "Down", null).Success.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspect_reads_a_disabled_control_as_not_keyboard_focusable()
    {
        var (window, disabled, enabled, _, _) = Build();
        try
        {
            UiAutomationDriver.Inspect(disabled, "Window/ComboBox[Region]").IsKeyboardFocusable.Should().BeFalse();
            UiAutomationDriver.Inspect(enabled, "Window/ComboBox[Language]").IsKeyboardFocusable.Should().BeTrue();
        }
        finally { window.Close(); }
    }
}
