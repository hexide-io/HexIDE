using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// <c>hover</c> with <c>stay</c> leaves the pointer where it went, so what is drawn only under the pointer, a flat toolbar
/// button's hot image or any <c>:pointerover</c> look, and an opened tip, can be snapshotted (#2562). A click raises its
/// events on the control and never sets pointer-over, and a hover's end takes the pointer out of the window, so nothing
/// else leaves it there.
/// </summary>
public class HoverStayTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button TippedButton(string content, string tip)
    {
        var button = new Button { Content = content, Width = 120, Height = 40 };
        ToolTip.SetTip(button, tip);
        ToolTip.SetShowDelay(button, 0);
        return button;
    }

    private static async Task Settle(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public async Task A_hover_left_by_stay_keeps_the_control_under_the_pointer_with_its_tip_open()
    {
        var button = TippedButton("Add", "Add Form");
        var window = Show(button);
        try
        {
            UiAutomationDriver.Hover(button, null, null, null, out var landing).Success.Should().BeTrue();
            await Settle(100);

            UiAutomationDriver.StayHover(landing!.Value.Receiver);
            await Settle(100);
            var said = UiAutomationDriver.StayedNote(landing.Value.Receiver, moved: null, saidAlready: false);

            said.Should().Be("; the pointer stays there and 'Button' reads as under it (isPointerOver), so take_snapshot "
                             + "shows its pointer-over look and any tip, until the next click, drag or hover in this window "
                             + "or the real mouse's next move over it");
            button.IsPointerOver.Should().BeTrue();
            ToolTip.GetIsOpen(button).Should().BeTrue("the tip a staying hover opened stays open for a snapshot");
            UiAutomationDriver.Inspect(button, "Window/Button").IsPointerOver.Should().BeTrue(
                "inspect_element reads it back");
        }
        finally { window.Close(); }
    }

    // The snapshot draws its owner's popups over the window, and the dump lists them under it; a tip was neither, so a
    // snapshot after a staying hover showed the hot look and not the tip the reply said was open.
    [AvaloniaFact]
    public async Task A_tip_a_staying_hover_left_open_is_one_of_its_owners_popups_and_the_dump_lists_its_text()
    {
        var button = TippedButton("Add", "Add Form");
        var window = Show(button);
        try
        {
            UiAutomationDriver.Hover(button, null, null, null, out var landing).Success.Should().BeTrue();
            await Settle(100);
            UiAutomationDriver.StayHover(landing!.Value.Receiver);

            UiAutomationDriver.AttachedPopupsOfPublic(button).Should().ContainSingle()
                .Which.Child.Should().BeOfType<ToolTip>();
            var dump = UiAutomationDriver.Dump(window, "Window", 10, interactiveOnly: false);
            UiAutomationDriver.Find(dump, "Add Form", 10).Matches.Should().NotBeEmpty();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_hover_that_does_not_stay_leaves_nothing_under_the_pointer()
    {
        var button = TippedButton("Add", "Add Form");
        var window = Show(button);
        try
        {
            UiAutomationDriver.Hover(button, null, null, null, out var landing).Success.Should().BeTrue();
            await Settle(100);
            button.IsPointerOver.Should().BeTrue("the move went through the window's input");

            UiAutomationDriver.EndHover(landing!.Value.Receiver);
            await Settle(100);

            button.IsPointerOver.Should().BeFalse();
            ToolTip.GetIsOpen(button).Should().BeFalse();
            UiAutomationDriver.Inspect(button, "Window/Button").IsPointerOver.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // A click raises its press and release on the control, which takes nothing out from under the pointer: without the
    // staying hover's end, the first button kept its pointer-over look and its tip over the second.
    [AvaloniaFact]
    public async Task The_next_click_in_the_window_ends_a_staying_hover_first()
    {
        var hovered = TippedButton("Add", "Add Form");
        var clicked = new Button { Content = "Other", Width = 120, Height = 40 };
        var window = Show(new StackPanel { Children = { hovered, clicked } });
        try
        {
            UiAutomationDriver.Hover(hovered, null, null, null, out var landing).Success.Should().BeTrue();
            await Settle(100);
            UiAutomationDriver.StayHover(landing!.Value.Receiver);

            var outcome = await UiAutomationDriver.InteractAsync(clicked, "click", null);
            await Settle(100);

            outcome.Success.Should().BeTrue(outcome.Error);
            hovered.IsPointerOver.Should().BeFalse("the click's pointer left what the hover was over");
            ToolTip.GetIsOpen(hovered).Should().BeFalse("a tip left open would cover what the click was aimed at");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task The_next_hover_in_the_window_ends_a_staying_hover_and_can_stay_itself()
    {
        var first = TippedButton("Add", "Add Form");
        var second = TippedButton("Remove", "Remove Form");
        var window = Show(new StackPanel { Children = { first, second } });
        try
        {
            UiAutomationDriver.Hover(first, null, null, null, out var landing).Success.Should().BeTrue();
            await Settle(100);
            UiAutomationDriver.StayHover(landing!.Value.Receiver);

            UiAutomationDriver.Hover(second, null, null, null, out var next).Success.Should().BeTrue();
            await Settle(100);
            UiAutomationDriver.StayHover(next!.Value.Receiver);
            await Settle(100);

            first.IsPointerOver.Should().BeFalse();
            ToolTip.GetIsOpen(first).Should().BeFalse();
            second.IsPointerOver.Should().BeTrue();
            ToolTip.GetIsOpen(second).Should().BeTrue();
        }
        finally { window.Close(); }
    }
}
