using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A tool window docks where <c>click</c>'s drag drops it (#2175). The held moves came in one burst, so Dock's drop
/// target, added on the first move, had no layout when the release looked for it, and the tool stayed where it was
/// while the reply read as a drag that worked.
/// </summary>
public class DragDockTests
{
    private sealed record Layout(Window Window, DockControl Host, ToolDock Left, ToolDock Right, Tool Alpha, Tool Beta);

    private static Layout Shown()
    {
        var factory = new Factory();
        var alpha = new Tool { Id = "Alpha", Title = "Alpha" };
        var beta = new Tool { Id = "Beta", Title = "Beta" };
        var left = new ToolDock
        {
            Id = "Left", Proportion = 0.5,
            VisibleDockables = factory.CreateList<IDockable>(alpha), ActiveDockable = alpha,
        };
        var right = new ToolDock
        {
            Id = "Right", Proportion = 0.5,
            VisibleDockables = factory.CreateList<IDockable>(beta), ActiveDockable = beta,
        };
        var columns = new ProportionalDock
        {
            Orientation = Dock.Model.Core.Orientation.Horizontal,
            VisibleDockables = factory.CreateList<IDockable>(left, new ProportionalDockSplitter(), right),
            ActiveDockable = left,
        };
        var root = new RootDock
        {
            VisibleDockables = factory.CreateList<IDockable>(columns), ActiveDockable = columns,
        };
        factory.InitLayout(root);
        var host = new DockControl { Layout = root };
        var window = new Window { Content = host, Width = 800, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return new Layout(window, host, left, right, alpha, beta);
    }

    private static ToolChromeControl ChromeOf(Layout layout, ToolDock dock) =>
        layout.Host.GetVisualDescendants().OfType<ToolChromeControl>().Single(c => ReferenceEquals(c.DataContext, dock));

    [AvaloniaFact]
    public async Task A_tool_dragged_by_its_title_onto_another_dock_moves_there()
    {
        var layout = Shown();
        try
        {
            var from = ChromeOf(layout, layout.Right);
            var onto = ChromeOf(layout, layout.Left);
            var centre = onto.TranslatePoint(new Point(onto.Bounds.Width / 2, onto.Bounds.Height / 2), from)!.Value;

            var pressed = await UiAutomationDriver.InteractAsync(from, "click",
                FormattableString.Invariant($"40,8 to {centre.X:0.#},{centre.Y:0.#}"));
            var outcome = await UiAutomationDriver.FinishAsync(pressed);

            outcome.Success.Should().BeTrue(outcome.Error);
            layout.Left.VisibleDockables.Should().Contain(layout.Beta);
            outcome.Detail.Should().EndWith("; Dock moved 'Beta' from a dock of its own to the dock with 'Alpha'");
        }
        finally { layout.Window.Close(); }
    }

    // A drop that docks nothing says so: it read the same as one that docked.
    [AvaloniaFact]
    public async Task A_drag_that_docks_nothing_says_the_layout_is_unchanged()
    {
        var layout = Shown();
        try
        {
            var from = ChromeOf(layout, layout.Right);

            var outcome = await UiAutomationDriver.FinishAsync(
                await UiAutomationDriver.InteractAsync(from, "click", "40,8 to 60,8"));

            outcome.Success.Should().BeTrue(outcome.Error);
            layout.Right.VisibleDockables.Should().Contain(layout.Beta);
            outcome.Detail.Should().EndWith("; Dock's layout is unchanged, so nothing was docked: the release was not over one "
                                            + "of Dock's drop targets, or dropped what it dragged where it was");
        }
        finally { layout.Window.Close(); }
    }
}
