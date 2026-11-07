using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// press_key types the character a key types, between its KeyDown and KeyUp, as a person's press does (#2428). It sent
/// the key events alone, so Shift+K on a running TextBox raised no KeyPress 75 and typed nothing, where the oracle's
/// key table has KeyPress between them (docs/vb6-fidelity-oracle/key-events.md, #2325).
/// </summary>
public class PressKeyTypesTests
{
    private static (Window Window, TextBox Box, List<string> Log) Show(bool focusable = true)
    {
        var box = new TextBox { Focusable = focusable };
        var window = new Window { Content = box };
        window.Show();
        box.Focus();
        Dispatcher.UIThread.RunJobs();
        var log = new List<string>();
        box.AddHandler(InputElement.KeyDownEvent, (_, e) => log.Add($"down {e.Key}"), RoutingStrategies.Bubble, handledEventsToo: true);
        box.AddHandler(InputElement.TextInputEvent, (_, e) => log.Add($"text {e.Text}"), RoutingStrategies.Tunnel, handledEventsToo: true);
        box.AddHandler(InputElement.KeyUpEvent, (_, e) => log.Add($"up {e.Key}"), RoutingStrategies.Bubble, handledEventsToo: true);
        return (window, box, log);
    }

    [AvaloniaTheory]
    [InlineData("K", null, "k", "down K|text k|up K")]
    [InlineData("K", "Shift", "K", "down LeftShift|down K|text K|up K|up LeftShift")]
    [InlineData("D1", "Shift", "!", "down LeftShift|down D1|text !|up D1|up LeftShift")]
    [InlineData("Space", null, " ", "down Space|text  |up Space")]
    public void A_key_that_types_a_character_types_it_between_its_down_and_up(
        string key, string? modifiers, string typed, string expected)
    {
        var (window, box, log) = Show();
        try
        {
            var outcome = UiAutomationDriver.PressKey(box, key, modifiers);
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("window-input");
            log.Should().Equal(expected.Split('|'));
            box.Text.Should().Be(typed);
            outcome.Detail.Should().EndWith($"; it typed \"{typed}\", as the key does");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("A", "Ctrl")]
    [InlineData("X", "Alt")]
    [InlineData("F2", null)]
    [InlineData("Enter", null)]
    [InlineData("Back", null)]
    [InlineData("Escape", null)]
    [InlineData("Tab", null)]
    [InlineData("LeftShift", null)]
    public void A_chord_a_function_key_or_a_control_character_types_nothing(string key, string? modifiers)
    {
        // A chord with Ctrl or Alt is a command, and a control character reaches a running program's KeyPress from the
        // key itself, as #2418's TextBox makes KeyAscii 1 from Ctrl+A: no platform sends either as text.
        var (window, _, log) = Show();
        try
        {
            var outcome = UiAutomationDriver.PressKey(window.Content as TextBox ?? throw new InvalidOperationException(), key, modifiers);

            outcome.Success.Should().BeTrue(outcome.Error);
            log.Should().NotContain(e => e.StartsWith("text ", StringComparison.Ordinal));
            outcome.Detail.Should().NotContain("it typed");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_routed_fallback_types_the_character_too()
    {
        var (window, _, log) = Show(focusable: false);
        try
        {
            var outcome = UiAutomationDriver.PressKey(window.Content as TextBox ?? throw new InvalidOperationException(), "K", "Shift");

            outcome.Mechanism.Should().Be("routed-key");
            log.Should().Equal("down LeftShift", "down K", "text K", "up K", "up LeftShift");
        }
        finally { window.Close(); }
    }
}
