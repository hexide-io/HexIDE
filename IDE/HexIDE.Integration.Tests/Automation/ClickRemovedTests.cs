using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class ClickRemovedTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_press_that_closes_its_window_stops_the_click(bool twice, bool closeOnSecond)
    {
        var pad = new Border { Background = Brushes.White };
        var window = new Window { Title = "Floating tool", Width = 200, Height = 100, Content = pad };
        var presses = new List<int>();
        var releases = 0;
        pad.PointerPressed += (_, e) =>
        {
            presses.Add(e.ClickCount);
            if (!closeOnSecond) Dispatcher.UIThread.Post(window.Close);
        };
        pad.DoubleTapped += (_, _) => { if (closeOnSecond) window.Close(); };
        window.AddHandler(InputElement.PointerReleasedEvent, (_, _) => releases++, RoutingStrategies.Bubble, handledEventsToo: true);
        Show(window);
        try
        {
            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(pad, "click", twice ? "10,10 double" : "10,10"));

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Contain("the window 'Floating tool' closed after the " + (closeOnSecond ? "second" : "first") + " press")
                .And.Contain("nothing was released there");
            presses.Should().Equal(closeOnSecond ? new[] { 1, 2 } : new[] { 1 });
            releases.Should().Be(closeOnSecond ? 1 : 0);
            window.PlatformImpl.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_press_that_removes_its_target_releases_to_the_control_under_it(bool twice, bool removeOnSecond)
    {
        var pad = new Border { Name = "Press pad", Background = Brushes.White };
        var host = new Border { Name = "Host", Background = Brushes.Gray, Child = pad };
        var window = new Window { Width = 200, Height = 100, Content = host };
        var presses = new List<int>();
        var releases = new List<object?>();
        pad.PointerPressed += (_, e) =>
        {
            presses.Add(e.ClickCount);
            if (e.ClickCount == (removeOnSecond ? 2 : 1))
                Dispatcher.UIThread.Post(() =>
                {
                    host.Child = null;
                    e.Pointer.Capture(null);
                });
        };
        window.AddHandler(InputElement.PointerReleasedEvent, (_, e) => releases.Add(e.Source), RoutingStrategies.Bubble, handledEventsToo: true);
        Show(window);
        try
        {
            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(host, "click", twice ? "10,10 double" : "10,10"));

            outcome.Success.Should().BeTrue(outcome.Error);
            presses.Should().Equal(removeOnSecond ? new[] { 1, 2 } : new[] { 1 });
            releases.Should().Equal(!twice ? new[] { host } : removeOnSecond ? new[] { pad, host } : new[] { host, host });
            outcome.Detail.Should().Contain("target 'Border[Press pad]' was removed after the " + (removeOnSecond ? "second" : "first") + " press")
                .And.Contain("release received by 'Border[Host]'").And.Contain("(under the release point, no capture)");
            window.IsVisible.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_release_point_in_an_open_window_still_fails(bool removeTarget)
    {
        var pad = new Border { Background = Brushes.White };
        var window = new Window { Width = 200, Height = 100, Content = pad };
        pad.PointerPressed += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            if (removeTarget) window.Content = null;
            e.Pointer.Capture(null);
        });
        Show(window);
        try
        {
            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(pad, "click", "10,10 to -900,-900"));

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("pressed the left button").And.Contain("nothing takes the pointer at the release point");
            if (removeTarget)
                outcome.Error.Should().Contain("the target 'Border' was removed after the first press");
            window.IsVisible.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    private static void Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
