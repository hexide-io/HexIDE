using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class KeyboardFocusTests
{
    [AvaloniaFact]
    public void Window_Tab_starts_at_the_second_control()
    {
        WithSecondFocused((window, _, second, third) =>
        {
            var moved = 0;
            second.LostFocus += (_, _) => moved++;
            var reply = UiAutomationDriver.PressKey(window, "Tab", null, "Window");
            reply.Success.Should().BeTrue(reply.Error);
            third.IsFocused.Should().BeTrue();
            moved.Should().Be(1);
            reply.Detail.Should().NotContain("moved focus");
        });
    }

    [AvaloniaFact]
    public void Direct_address_reports_the_focus_move_before_the_key()
    {
        WithSecondFocused((_, _, second, third) =>
        {
            var events = new List<string>();
            second.LostFocus += (_, _) => events.Add("lost");
            third.GotFocus += (_, _) => events.Add("got");
            third.KeyDown += (_, _) => events.Add("key");
            var reply = UiAutomationDriver.PressKey(third, "F12", null, "Window/Button[Third]");
            reply.Success.Should().BeTrue(reply.Error);
            reply.Detail.Should().Contain("moved focus from Window/Button[Second] to Window/Button[Third]");
            events.Should().Equal("lost", "got", "key");
        });
    }

    [AvaloniaFact]
    public void Inspection_reports_only_the_control_that_has_keyboard_focus()
    {
        WithSecondFocused((window, first, second, third) =>
        {
            foreach (var control in new[] { first, second, third })
            {
                var json = JsonSerializer.SerializeToElement(UiAutomationDriver.Inspect(control, "Window", window),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                json.GetProperty("isKeyboardFocusable").GetBoolean().Should().BeTrue();
                json.TryGetProperty("isFocused", out var focused).Should().BeTrue();
                focused.GetBoolean().Should().Be(ReferenceEquals(control, second));
            }
        });
    }

    [AvaloniaFact]
    public void Dump_focus_metadata_names_the_focused_path_and_explains_no_focus()
    {
        WithSecondFocused((window, _, second, _) =>
        {
            // The same window-wide metadata builder supplies both tree and match replies.
            var method = typeof(UiAutomationDriver).GetMethod("FocusIn");
            method.Should().NotBeNull("dump replies need window-wide focus metadata");
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var reply = JsonSerializer.SerializeToElement(method!.Invoke(null, [window]), options);
            reply.GetProperty("focusedPath").GetString().Should().Be("Window/Button[Second]");
            UiAutomationDriver.Resolve(window, reply.GetProperty("focusedPath").GetString()!).control.Should().BeSameAs(second);
            window.FocusManager!.Focus(null);
            reply = JsonSerializer.SerializeToElement(method.Invoke(null, [window]), options);
            reply.GetProperty("focusedPath").ValueKind.Should().Be(JsonValueKind.Null);
            reply.GetProperty("focusReason").GetString().Should().Be("No control in this window has keyboard focus.");
        });
    }

    private static void WithSecondFocused(Action<Window, Button, Button, Button> check)
    {
        var first = new Button { Name = "First", Content = "First" };
        var second = new Button { Name = "Second", Content = "Second" };
        var third = new Button { Name = "Third", Content = "Third" };
        var window = new Window { Content = new StackPanel { Children = { first, second, third } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            second.Focus().Should().BeTrue();
            second.IsFocused.Should().BeTrue();
            check(window, first, second, third);
        }
        finally { window.Close(); }
    }
}
