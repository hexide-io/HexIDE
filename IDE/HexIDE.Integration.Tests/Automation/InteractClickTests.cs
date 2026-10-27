using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// <c>interact</c>'s <c>click</c> (#1033): the one verb that presses and releases a pointer, so a control's own press and
/// release handlers run as they do for a person. <c>invoke</c> clicks through a peer and raises no pointer event.
/// </summary>
public class InteractClickTests
{
    private sealed record Scene(Window Window, Button Button, Border Pad, Border Cover, Border Under);

    // A Button, a Border that records its pointer events, and a Border lying under another one.
    private static Scene Build(bool namedPads = false, string? buttonName = null)
    {
        var button = new Button { Name = buttonName, Content = "go", Width = 80, Height = 30 };
        var pad = new Border { Name = namedPads ? "Press pad" : null, Background = Brushes.White, Width = 100, Height = 60 };
        var under = new Border { Background = Brushes.White, Width = 60, Height = 40 };
        var cover = new Border { Name = namedPads ? "Release pad" : null, Background = Brushes.Gray, Width = 60, Height = 40 };
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
        // click hit-tests as a real press does, against what has been rendered, so a frame must exist first. Interact, which
        // these call, takes the window as it is; the tool goes through InteractAsync, which waits for the frame (#1065).
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return new Scene(window, button, pad, cover, under);
    }

    [AvaloniaTheory]
    [InlineData(null, "Button[Standard.OpenProject]")]
    [InlineData("Open", "Button[Open]")]
    public async Task Click_names_an_automation_id_and_preserves_an_Avalonia_name(string? name, string described)
    {
        var scene = Build(buttonName: name);
        try
        {
            Avalonia.Automation.AutomationProperties.SetAutomationId(scene.Button, "Standard.OpenProject");
            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Button, "click", null));
            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Contain($"on '{described}'");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_click_presses_and_releases_so_a_Button_raises_Click_from_its_release()
    {
        var scene = Build();
        try
        {
            var seen = new List<string>();
            scene.Button.AddHandler(InputElement.PointerPressedEvent, (_, _) => seen.Add("pressed"), RoutingStrategies.Bubble, handledEventsToo: true);
            scene.Button.Click += (_, _) => seen.Add("click");
            scene.Button.AddHandler(InputElement.PointerReleasedEvent, (_, _) => seen.Add("released"), RoutingStrategies.Bubble, handledEventsToo: true);

            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Button, "click", null));

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("pointer");
            // The Button raises Click inside its own release handler, which runs before an instance handler.
            seen.Should().Equal("pressed", "click", "released");
            outcome.Detail.Should().Contain("clicked the left button at (40, 15) on '").And.Contain("in the window");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_click_carries_the_point_the_button_and_the_keys_held()
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

            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Pad, "click", "10,5.5 right shift ctrl"));

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
    public async Task A_click_reply_uses_invariant_coordinates_under_a_comma_culture()
    {
        var scene = Build();
        try
        {
            scene.Pad.Width = 180;
            scene.Window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var expectedCulture = CultureInfo.CurrentCulture.Name;
            InteractOutcome pressed;
            DispatcherOperation<string> queuedCulture;
            // DoClick parses the point and formats the entire reply synchronously, including the text its release
            // reuses. Do not pump jobs or await under de-DE: Avalonia restores a queued operation's ExecutionContext
            // without reverting it afterwards. Suppress capture too, so work posted by the press cannot retain de-DE.
            using (ExecutionContext.SuppressFlow())
            {
                var originalCulture = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                    pressed = UiAutomationDriver.Interact(scene.Pad, "click", "150.5,20");
                    // Exercise deferred work too, without relying on a particular hit target or dispatcher timing.
                    queuedCulture = Dispatcher.UIThread.InvokeAsync(() => CultureInfo.CurrentCulture.Name);
                }
                finally { CultureInfo.CurrentCulture = originalCulture; }
            }
            var outcome = await UiAutomationDriver.FinishAsync(pressed);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Contain("(150.5, 20)").And.Contain("(160.5, 80)")
                .And.NotContain("150,5");
            (await queuedCulture).Should().Be(expectedCulture, "work queued under de-DE must not retain it");
            CultureInfo.CurrentCulture.Name.Should().Be(expectedCulture);
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_click_on_a_covered_target_reaches_what_covers_it_and_says_so()
    {
        // As a real click does: the press goes to the topmost element at the point, and the reply names it.
        var scene = Build();
        try
        {
            var underPressed = 0;
            var coverPressed = 0;
            scene.Under.PointerPressed += (_, _) => underPressed++;
            scene.Cover.PointerPressed += (_, _) => coverPressed++;

            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Under, "click", null));

            outcome.Success.Should().BeTrue(outcome.Error);
            (underPressed, coverPressed).Should().Be((0, 1));
            outcome.Detail.Should().Contain("the press landed on '").And.Contain("which covers the target there");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_click_outside_the_target_reaches_another_control_and_says_it_is_outside()
    {
        var scene = Build();
        try
        {
            var padPressed = 0;
            var coverPressed = 0;
            scene.Pad.PointerPressed += (_, _) => padPressed++;
            scene.Cover.PointerPressed += (_, _) => coverPressed++;

            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Pad, "click", "220,20"));

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("pointer");
            (padPressed, coverPressed).Should().Be((0, 1));
            outcome.Detail.Should().Be("clicked the left button at (220, 20) on 'Border', which is (230, 80) in the window; "
                + "(220, 20) is outside 'Border', which is 100 × 60, and the press landed on 'Border'");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaFact]
    public async Task What_the_press_set_off_runs_before_the_release()
    {
        // A person's release comes after the press has had its effect. Raised in the same dispatcher job, a running form's
        // MouseUp came before its MouseDown, whose handler had yielded its time slice and queued the rest of itself (#766).
        var scene = Build();
        try
        {
            var seen = new List<string>();
            scene.Pad.PointerPressed += (_, _) =>
            {
                seen.Add("pressed");
                Dispatcher.UIThread.Post(() => seen.Add("queued by the press"));
            };
            scene.Pad.PointerReleased += (_, _) => seen.Add("released");

            var pressed = UiAutomationDriver.Interact(scene.Pad, "click", null);

            seen.Should().Equal("pressed");
            pressed.Release.Should().NotBeNull();
            pressed.Detail.Should().StartWith("pressed the left button at (50, 30) on '").And.EndWith("the release is still to come");

            var outcome = await UiAutomationDriver.FinishAsync(pressed);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Release.Should().BeNull("the reply is the finished click");
            outcome.Detail.Should().StartWith("clicked the left button at (50, 30) on '");
            seen.Should().Equal("pressed", "queued by the press", "released");
        }
        finally { scene.Window.Close(); }
    }

    // A window shown and laid out a moment ago, which has drawn no frame yet: no dispatcher job has run since it was shown,
    // so the render timer has had no chance to tick (#1065).
    private static (Window Window, Button Button) ShownButNotDrawn()
    {
        var button = new Button { Content = "go", Width = 80, Height = 30 };
        var window = new Window { Content = button, Width = 200, Height = 100 };
        window.Show();
        window.UpdateLayout();
        return (window, button);
    }

    [AvaloniaFact]
    public async Task A_click_on_a_window_shown_a_moment_ago_waits_for_its_first_frame()
    {
        var (window, button) = ShownButNotDrawn();
        try
        {
            var clicks = 0;
            button.Click += (_, _) => clicks++;

            // What #1065 saw: laid out, so the point is inside the button, and refused, because nothing has been drawn.
            var undrawn = UiAutomationDriver.Interact(button, "click", null);
            undrawn.Success.Should().BeFalse("the window has drawn nothing for the press to be hit-tested against");
            undrawn.Error.Should().StartWith("nothing takes the pointer at (40, 15) on '");

            var outcome = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(button, "click", null));

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().StartWith("clicked the left button at (40, 15) on '");
            clicks.Should().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_click_on_a_window_that_draws_nothing_in_time_is_refused_and_says_why()
    {
        var (window, button) = ShownButNotDrawn();
        try
        {
            var pressed = 0;
            button.AddHandler(InputElement.PointerPressedEvent, (_, _) => pressed++, RoutingStrategies.Tunnel, handledEventsToo: true);

            var outcome = await UiAutomationDriver.InteractAsync(button, "click", null, frameWait: TimeSpan.Zero);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("nothing takes the pointer at (40, 15) on '")
                .And.Contain("or its window, which drew no frame in the 0 ms the click waited, has nothing drawn there yet")
                .And.EndWith("try again once it shows");
            pressed.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Waiting_for_a_frame_leaves_every_other_action_as_it_was()
    {
        var (window, button) = ShownButNotDrawn();
        try
        {
            var clicks = 0;
            button.Click += (_, _) => clicks++;

            // invoke goes through the peer, which needs nothing drawn, and does not wait: a zero wait would refuse a click.
            var outcome = await UiAutomationDriver.InteractAsync(button, "invoke", null, frameWait: TimeSpan.Zero);

            outcome.Success.Should().BeTrue(outcome.Error);
            clicks.Should().Be(1);
        }
        finally { window.Close(); }
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

    [AvaloniaFact]
    public async Task A_drag_draws_a_toolbox_control_from_the_press_to_the_release()
    {
        var toolbox = new ToolBoxToolViewModel(
            Substitute.For<IDocumentDockService>(), Substitute.For<IComponentRegistry>(),
            Substitute.For<ILocalizationService>());
        toolbox.SelectedComponent = System.Linq.Enumerable.First(toolbox.Components,
            c => c.BaseClass == CommandButtonComponentClass.Instance);
        using var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>())
            .Initialize(new FormDefinition(
                new ProjectDefinition(VBProjectType.EXE, "P"),
                FormComponentClass.Instance, "Form1"));
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var canvas = view.FindControl<Canvas>("NewControlSpawner")!;

            var outcome = await UiAutomationDriver.FinishAsync(
                UiAutomationDriver.Interact(canvas, "click", "20,20 to 100,60"));

            outcome.Success.Should().BeTrue(outcome.Error);
            vm.Components.Should().ContainSingle();
            var drawn = vm.Components[0];
            // One pixel past the release, as vb6.exe's designer draws it (#2058).
            new Rect(drawn.Left, drawn.Top, drawn.Width, drawn.Height).Should().Be(new Rect(20, 20, 81, 41));
            outcome.Detail.Should().Contain("dragged").And.Contain("Canvas[NewControlSpawner]")
                .And.Contain("(20, 20)").And.Contain("(100, 60)").And.Contain("pointer capturer");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_drag_routes_held_moves_and_release_to_capture_or_the_control_under_the_point(bool capture)
    {
        var scene = Build(namedPads: true);
        try
        {
            Avalonia.Automation.AutomationProperties.SetAutomationId(scene.Cover, "Release pad");
            var seen = new List<string>();
            var moves = new List<Point>();
            scene.Pad.PointerPressed += (_, e) =>
            {
                e.Pointer.Capture(capture ? scene.Pad : null);
                Dispatcher.UIThread.Post(() => seen.Add("press work"));
            };
            scene.Pad.PointerMoved += (_, e) =>
            {
                seen.Should().Contain("press work");
                e.GetCurrentPoint(scene.Pad).Properties.IsRightButtonPressed.Should().BeTrue();
                e.KeyModifiers.Should().Be(KeyModifiers.Shift);
                moves.Add(e.GetPosition(scene.Window));
            };
            Control? recipient = null;
            scene.Pad.PointerReleased += (_, _) => recipient = scene.Pad;
            scene.Cover.PointerReleased += (_, _) => recipient = scene.Cover;
            // A complete discovery path, including spaces, can be pasted before the colon.
            static UiNode? Find(UiNode node) => node.AutomationId == "Release pad" ? node
                : System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.Select(node.Children, Find), n => n is not null);
            var path = Find(UiAutomationDriver.Dump(scene.Window, "Window", 10, false))!.Path;
            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(scene.Pad, "click",
                $"10,5 right shift to {path}:20,10"));
            outcome.Success.Should().BeTrue(outcome.Error);
            recipient.Should().Be(capture ? scene.Pad : scene.Cover);
            if (capture)
            {
                moves.Count.Should().BeGreaterThan(1, "a drag sends several held moves");
                moves.Should().EndWith(new Point(220, 70));
            }
            outcome.Detail.Should().Contain("(10, 5)").And.Contain("(20, 10)")
                .And.Contain(capture ? "Border[Press pad]" : "Border[Release pad]")
                .And.Contain(capture ? "pointer capturer" : "under the release point, no capture");
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("double to 20,10")]
    [InlineData("10,5 to 20,10 double")]
    public void Double_and_to_are_refused_before_pressing(string value)
    {
        var scene = Build();
        try
        {
            var pressed = false;
            scene.Pad.PointerPressed += (_, _) => pressed = true;
            var outcome = UiAutomationDriver.Interact(scene.Pad, "click", value);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("double cannot be combined with to").And.Contain("a double click repeats at one point");
            pressed.Should().BeFalse();
        }
        finally { scene.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("10", "'10' is not a point, a button, a key or double")]
    [InlineData("1,2,3", "'1,2,3' is not a point")]
    [InlineData("1,2 3,4", "'3,4' is not a point")]
    [InlineData("left right", "names two")]
    [InlineData("wiggle", "'wiggle' is not a point, a button, a key or double")]
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
