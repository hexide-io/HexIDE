using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The Locals and Watches trees name each item after its expression, so automation can address and select it by
/// its text (#714).
/// </summary>
/// <remarks>
/// The names come from a <c>ReflectionBinding</c> in each view's <c>TreeViewItem</c> style. Unlike the compiled
/// bindings in the templates, a rename of <c>Expression</c> would not break the build; it would leave the items
/// unnamed and every reply calling them 'TreeItem'. These tests are what catches that.
/// </remarks>
public class DebugTreeItemNameTests
{
    private static Window Show(Control view)
    {
        var window = new Window { Content = view, Width = 500, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static string?[] ItemNames(Window window) =>
        [.. window.GetVisualDescendants().OfType<TreeViewItem>().Select(AutomationProperties.GetName)];

    [AvaloniaFact]
    public void LocalsItems_AreNamedAfterTheirExpression_ChildrenToo()
    {
        var debug = Substitute.For<IDebugController>();
        debug.GetLocals().Returns(new DebugScope("Module1.Go",
        [
            new DebugNode("obj", string.Empty, "Thing", () => [new DebugNode("field", "1", "Integer")]),
            new DebugNode("i", "3", "Integer"),
        ]));
        var vm = new LocalsToolViewModel(Substitute.For<ILocalizationService>(), debug);
        debug.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 7));
        var window = Show(new LocalsToolView { DataContext = vm });
        try
        {
            ItemNames(window).Should().Equal("obj", "i");

            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            var obj = window.GetVisualDescendants().OfType<TreeViewItem>().First();
            UiAutomationDriver.Interact(obj, "expand", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();

            ItemNames(window).Should().Contain("field");
            UiAutomationDriver.Interact(tree, "select", "field").Success.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WatchItems_AreNamedAfterTheirExpression()
    {
        var watches = new WatchService();
        watches.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        watches.Add(new WatchExpression("total", WatchType.Expression, "Module1.Go"));
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var vm = new WatchesToolViewModel(localization, watches, Substitute.For<IDebugController>(),
            Substitute.For<IWindowManager>());
        var window = Show(new WatchesToolView { DataContext = vm });
        try
        {
            ItemNames(window).Should().Equal("count", "total");

            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            UiAutomationDriver.Interact(tree, "select", "total").Success.Should().BeTrue();
            vm.SelectedRow!.Expression.Should().Be("total");
        }
        finally { window.Close(); }
    }
}
