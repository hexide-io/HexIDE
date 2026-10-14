using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// <c>interact</c>'s <c>click</c> (#1033): the one verb that presses and releases a pointer, so a control's own press and
/// release handlers run as they do for a person. <c>invoke</c> clicks through a peer and raises no pointer event.
/// </summary>
public class InteractClickTests
{
    private sealed record Scene(Window Window, Button Button, Border Pad, Border Cover, Border Under);

    // A Button, a Border that records its pointer events, and a Border lying under another one.
    private static Scene Build()
    {
        var button = new Button { Content = "go", Width = 80, Height = 30 };
        var pad = new Border { Background = Brushes.White, Width = 100, Height = 60 };
        var under = new Border { Background = Brushes.White, Width = 60, Height = 40 };
        var cover = new Border { Background = Brushes.Gray, Width = 60, Height = 40 };
        var canvas = new Canvas { Width = 400, Height = 300 };
        foreach (var (control, left, top) in new (Control, double, double)[] { (button, 10, 10), (pad, 10, 60), (under, 200, 60), (cover, 200, 60) })
        {
            Canvas.SetLeft(control, left);
            Canvas.SetTop(control, top);
            canvas.Children.Add(control);
        }
        var window = new Window { Content = canvas, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // click hit-tests as a real press does, against what has been rendered, so a frame must exist first. A running
        // IDE has rendered long before a call arrives; a headless window shown a moment ago has not.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return new Scene(window, button, pad, cover, under);
    }

    [AvaloniaFact]
    public void A_click_presses_and_releases_so_a_Button_raises_Click_from_its_release()
    {
        var scene = Build();
        try
        {
            var seen = new List<string>();
            scene.Button.AddHandler(InputElement.PointerPressedEvent, (_, _) => seen.Add("pressed"), RoutingStrategies.Bubble, handledEventsToo: true);
            scene.Button.Click += (_, _) => seen.Add("click");
            scene.Button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => seen.Add("released"), RoutingStrategies.Bubble, handledEventsToo: true);

            var outcome = UiAutomationDriver.Interact(scene.Button, "click", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("pointer");
            // The Button raises Click inside its own release handler, which runs before an instance handler.
            seen.Should().Equal("pressed", "click", "released");
            outcome.Detail.Should().Contain("clicked the left button at (40, 15) on '").And.Contain("in the window");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaFact]
    public void A_click_carries_the_point_the_button_and_the_keys_held()
    {
        var scene = Build();
        try
        {
            Point? at = null;
            PointerUpdateKind? pressKind = null, releaseKind = null;
            KeyModifiers? keys = null;
            MouseButton? released = null;
            scene.Pad.PointerPressed += (_, e) =>
            {
                at = e.GetPosition(scene.Pad);
                pressKind = e.GetCurrentPoint(scene.Pad).Properties.PointerUpdateKind;
                keys = e.KeyModifiers;
            };
            scene.Pad.PointerReleased += (_, e) =>
            {
                releaseKind = e.GetCurrentPoint(scene.Pad).Properties.PointerUpdateKind;
                released = e.InitialPressMouseButton;
            };

            var outcome = UiAutomationDriver.Interact(scene.Pad, "click", "10,5.5 right shift ctrl");

            outcome.Success.Should().BeTrue(outcome.Error);
            at.Should().Be(new Point(10, 5.5));
            pressKind.Should().Be(PointerUpdateKind.RightButtonPressed);
            releaseKind.Should().Be(PointerUpdateKind.RightButtonReleased);
            released.Should().Be(MouseButton.Right);
            keys.Should().Be(KeyModifiers.Shift | KeyModifiers.Control);
            outcome.Detail.Should().Contain("clicked the right button holding shift and ctrl at (10, 5.5) on '")
                .And.Contain("which is (20, 65.5) in the window");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaFact]
    public void A_click_on_a_covered_target_reaches_what_covers_it_and_says_so()
    {
        // As a real click does: the press goes to the topmost element at the point, and the reply names it.
        var scene = Build();
        try
        {
            var underPressed = 0;
            var coverPressed = 0;
            scene.Under.PointerPressed += (_, _) => underPressed++;
            scene.Cover.PointerPressed += (_, _) => coverPressed++;

            var outcome = UiAutomationDriver.Interact(scene.Under, "click", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            (underPressed, coverPressed).Should().Be((0, 1));
            outcome.Detail.Should().Contain("the press landed on '").And.Contain("which covers the target there");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaFact]
    public void A_click_where_nothing_takes_the_pointer_is_refused()
    {
        var scene = Build();
        try
        {
            var pressed = 0;
            scene.Pad.PointerPressed += (_, _) => pressed++;

            var outcome = UiAutomationDriver.Interact(scene.Pad, "click", "-900,-900");

            outcome.Success.Should().BeFalse();
            outcome.Mechanism.Should().Be("pointer");
            outcome.Error.Should().Contain("nothing takes the pointer at (-900, -900) on '").And.Contain("nothing was pressed");
            pressed.Should().Be(0);
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("10", "'10' is not a point, a button or a key")]
    [InlineData("1,2,3", "'1,2,3' is not a point")]
    [InlineData("1,2 3,4", "'3,4' is not a point")]
    [InlineData("left right", "names two")]
    [InlineData("wiggle", "'wiggle' is not a point, a button or a key")]
    public void A_value_click_cannot_read_is_refused_with_what_it_takes(string value, string why)
    {
        var scene = Build();
        try
        {
            var outcome = UiAutomationDriver.Interact(scene.Pad, "click", value);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain(why).And.Contain(UiAutomationDriver.ClickValue);
        }
        finally { scene.Window.Close(); }
    }
}
