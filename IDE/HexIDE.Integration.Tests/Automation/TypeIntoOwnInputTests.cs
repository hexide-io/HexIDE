using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// type_text's keyboard mode sends its keys to a control that takes typing in handlers of its own, as a running form's
/// RichTextBox does, and judges it by what it did with them, not by its class (#2460). It was refused as having "no text
/// surface (TextEditor/TextArea/TextBox)", so typing into such a control could not be driven at all.
/// </summary>
public class TypeIntoOwnInputTests
{
    /// <summary>A control drawn by HexIDE that takes typing itself, as <c>VBRichTextBox</c> does.</summary>
    private sealed class OwnInput : Control
    {
        public readonly List<string> Seen = [];
        public string Text = "";
        public bool TakesText = true;

        public OwnInput() => Focusable = true;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            Seen.Add($"down {e.Key}");
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            Seen.Add($"text {e.Text}");
            if (!TakesText)
                return;
            Text += e.Text;
            e.Handled = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            Seen.Add($"up {e.Key}");
        }
    }

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public async Task Keys_go_to_a_control_that_takes_typing_itself_in_the_order_a_keyboard_sends_them()
    {
        var target = new OwnInput { Width = 100, Height = 40 };
        var window = Show(target);
        try
        {
            var outcome = await UiAutomationDriver.TypeKeysAsync(target, "aB");

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be(UiAutomationDriver.KeyboardMechanism);
            outcome.Detail.Should().Be("typed 2 key(s) into OwnInput as a person types them; LeftShift went down before the capital and up after it");
            target.Text.Should().Be("aB");
            target.IsFocused.Should().BeTrue("keys go where focus is, and a person clicks into the control first");
            target.Seen.Should().Equal("down A", "text a", "up A", "down LeftShift", "down B", "text B", "up B", "up LeftShift");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_control_that_takes_none_of_the_text_is_reported_from_what_came_back()
    {
        var target = new OwnInput { Width = 100, Height = 40, TakesText = false };
        var window = Show(target);
        try
        {
            var outcome = await UiAutomationDriver.TypeKeysAsync(target, "ab");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Be("sent 2 key(s) to OwnInput as a person types them, and it took the text of none of them: it "
                + "handled no TextInput, so it takes no typing. Their KeyDown and KeyUp were raised, and anything those drive ran");
            target.Seen.Should().Equal("down A", "text a", "up A", "down B", "text b", "up B");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Enter_alone_types_no_character_so_the_control_is_not_judged_on_text()
    {
        var target = new OwnInput { Width = 100, Height = 40, TakesText = false };
        var window = Show(target);
        try
        {
            var outcome = await UiAutomationDriver.TypeKeysAsync(target, "\n");

            outcome.Success.Should().BeTrue(outcome.Error);
            // Key.Enter and Key.Return are one value, which prints as Return.
            target.Seen.Should().Equal("down Return", "up Return");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_control_that_cannot_take_focus_is_still_refused_and_says_why()
    {
        var target = new OwnInput { Width = 100, Height = 40, Focusable = false };
        var window = Show(target);
        try
        {
            var outcome = await UiAutomationDriver.TypeKeysAsync(target, "a");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Be("'OwnInput' has no text surface (TextEditor/TextArea/TextBox) to type into, and does not take "
                + "focus, so a person's keys could not reach it");
            target.Seen.Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Document_mode_still_needs_a_text_box_and_points_at_keyboard_mode()
    {
        var target = new OwnInput { Width = 100, Height = 40 };
        var window = Show(target);
        try
        {
            var outcome = UiAutomationDriver.TypeText(target, "a");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Be("'OwnInput' has no text surface (TextEditor/TextArea/TextBox) to type into; mode \"keyboard\" "
                + "sends it keys as a person types, for a control that takes typing itself");
            target.Seen.Should().BeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_disabled_control_is_refused_before_any_key_is_sent()
    {
        var target = new OwnInput { Width = 100, Height = 40, IsEnabled = false };
        var window = Show(target);
        try
        {
            var outcome = await UiAutomationDriver.TypeKeysAsync(target, "a");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Be("OwnInput is disabled, so a person could not type there either; nothing was typed");
            target.Seen.Should().BeEmpty();
        }
        finally { window.Close(); }
    }
}
