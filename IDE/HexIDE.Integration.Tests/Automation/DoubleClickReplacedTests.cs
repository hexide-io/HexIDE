using System.Collections.Generic;
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

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A double click's second press lands where the first did, on whatever is there by then (#2324). A Toolbar rebuilds its
/// buttons on each release, so what the first press landed on was gone, and the second click was refused as "hidden or
/// closed, or detached": the Toolbar's measured double-click (#1088) couldn't be checked live.
/// </summary>
public class DoubleClickReplacedTests
{
    // A bar that replaces its button on every release, as VBToolbar.Release → RefreshButtons does.
    private sealed class RebuildingBar : Border
    {
        public readonly List<(string Button, int Count)> Presses = [];
        private int built;

        public RebuildingBar()
        {
            Background = Brushes.White;
            Rebuild();
            AddHandler(PointerPressedEvent, (_, e) => Presses.Add((((Control)e.Source!).Name!, e.ClickCount)),
                RoutingStrategies.Bubble, handledEventsToo: true);
            AddHandler(PointerReleasedEvent, (_, _) => Dispatcher.UIThread.Post(Rebuild), RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        public void Rebuild() => Child = new Border
        {
            Name = $"Button{++built}", Width = 40, Height = 30, Background = Brushes.Gray,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_second_press_goes_to_what_replaced_the_first_ones_landing(bool addressTheButton)
    {
        var bar = new RebuildingBar { Width = 200, Height = 30 };
        var window = new Window { Width = 300, Height = 200, Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var target = addressTheButton ? (Control)bar.Child! : bar;

            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(target, "click", "10,10 double"));

            outcome.Success.Should().BeTrue(outcome.Error);
            bar.Presses.Should().Equal(("Button1", 1), ("Button2", 2));
            outcome.Detail.Should().StartWith("double-clicked the left button")
                .And.Contain("; the first click replaced what it landed on, so the second press went to")
                .And.Contain("which is at that point now");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_double_click_on_a_control_that_stays_says_nothing_of_a_replacement()
    {
        var pad = new Border { Width = 100, Height = 40, Background = Brushes.White };
        var counts = new List<int>();
        pad.PointerPressed += (_, e) => counts.Add(e.ClickCount);
        var window = new Window { Width = 300, Height = 200, Content = pad };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(pad, "click", "10,10 double"));

            outcome.Success.Should().BeTrue(outcome.Error);
            counts.Should().Equal(1, 2);
            outcome.Detail.Should().NotContain("replaced");
        }
        finally { window.Close(); }
    }
}
