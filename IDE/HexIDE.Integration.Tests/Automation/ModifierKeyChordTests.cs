using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// press_key and type_text press each modifier's own key around the key, as a person's chord does (#2422). Only the key
/// went before, carrying its modifiers as flags, so a program never saw Shift's KeyDown 16 or its KeyUp with mask 0,
/// which the oracle measures (docs/vb6-fidelity-oracle/key-events.md, #2325).
/// </summary>
public class ModifierKeyChordTests
{
    private static (Window Window, TextBox Box, List<string> Log) Show(bool focusable = true)
    {
        var box = new TextBox { Focusable = focusable };
        var window = new Window { Content = box };
        window.Show();
        // Where a person's keys already go, so a reply names no focus move.
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        var log = new List<string>();
        box.AddHandler(InputElement.KeyDownEvent, (_, e) => log.Add($"down {e.Key} {e.KeyModifiers}"),
            RoutingStrategies.Bubble, handledEventsToo: true);
        box.AddHandler(InputElement.KeyUpEvent, (_, e) => log.Add($"up {e.Key} {e.KeyModifiers}"),
            RoutingStrategies.Bubble, handledEventsToo: true);
        return (window, box, log);
    }

    [AvaloniaTheory]
    [InlineData("K", "Shift", "down LeftShift Shift|down K Shift|up K Shift|up LeftShift None")]
    [InlineData("A", "Ctrl", "down LeftCtrl Control|down A Control|up A Control|up LeftCtrl None")]
    [InlineData("X", "Alt", "down LeftAlt Alt|down X Alt|up X Alt|up LeftAlt None")]
    [InlineData("A", "Ctrl+Shift",
        "down LeftCtrl Control|down LeftShift Control, Shift|down A Control, Shift|up A Control, Shift|up LeftShift Control|up LeftCtrl None")]
    [InlineData("F2", null, "down F2 None|up F2 None")]
    public void A_chord_presses_each_modifier_s_own_key_around_the_key(string key, string? modifiers, string expected)
    {
        var (window, box, log) = Show();
        try
        {
            var outcome = UiAutomationDriver.PressKey(box, key, modifiers);
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("window-input");
            log.Should().Equal(expected.Split('|'));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_modifier_pressed_alone_reports_itself_held_going_down_and_not_coming_up()
    {
        // As Windows reports it and VB6 shows it: Shift's KeyDown 16 has mask 1, its KeyUp mask 0.
        var (window, box, log) = Show();
        try
        {
            var outcome = UiAutomationDriver.PressKey(box, "LeftShift", null);
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            log.Should().Equal("down LeftShift Shift", "up LeftShift None");
            outcome.Detail.Should().Be("pressed LeftShift");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_reply_names_the_modifier_keys_pressed_around_the_key()
    {
        var (window, box, _) = Show();
        try
        {
            UiAutomationDriver.PressKey(box, "K", "Shift").Detail.Should().Be(
                "pressed Shift+K; LeftShift went down before K and up after it, as a person holds it");
            UiAutomationDriver.PressKey(box, "A", "Ctrl+Shift").Detail.Should().Be(
                "pressed Control+Shift+A; LeftCtrl and LeftShift went down before A, in that order, and up after it in "
                + "reverse, as a person holds them");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_routed_fallback_presses_the_modifier_s_own_key_too()
    {
        var (window, box, log) = Show(focusable: false);
        try
        {
            var outcome = UiAutomationDriver.PressKey(box, "K", "Shift");

            outcome.Mechanism.Should().Be("routed-key");
            log.Should().Equal("down LeftShift Shift", "down K Shift", "up K Shift", "up LeftShift None");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Typing_a_capital_presses_Shift_s_own_key_around_it_and_says_so()
    {
        var (window, box, log) = Show();
        try
        {
            var typed = await UiAutomationDriver.TypeKeysAsync(box, "Ab", ct: TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            typed.Success.Should().BeTrue(typed.Error);
            box.Text.Should().Be("Ab");
            log.Should().Equal("down LeftShift Shift", "down A Shift", "up A Shift", "up LeftShift None",
                "down B None", "up B None");
            typed.Detail.Should().EndWith("; LeftShift went down before the capital and up after it");
        }
        finally { window.Close(); }
    }
}
