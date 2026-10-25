using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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

    [AvaloniaTheory]
    [InlineData("delete")]
    [InlineData("edit")]
    [InlineData("key")]
    public async Task WatchChildSelection_DoesNotActOnPreviousWatch(string action)
    {
        var watches = new WatchService();
        watches.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        watches.Add(new WatchExpression("obj", WatchType.Expression, "Module1.Go"));
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        var manager = Substitute.For<IWindowManager>();
        var vm = new WatchesToolViewModel(localization, watches, Substitute.For<IDebugController>(), manager);
        vm.Rows[1].Update(new DebugEvalResult(true, "", "Thing", false,
            new DebugNode("obj", "", "Thing", () => [new DebugNode("field", "1", "Integer")])));
        vm.Rows[1].IsExpanded = true;
        var view = new WatchesToolView { DataContext = vm };
        var window = Show(view);
        try
        {
            var tree = window.GetVisualDescendants().OfType<TreeView>().Single();
            tree.SelectedItem = vm.Rows[0];
            vm.SelectedRow.Should().BeSameAs(vm.Rows[0]);
            var child = vm.Rows[1].Children.Single();
            tree.SelectedItem = child;
            Dispatcher.UIThread.RunJobs();

            switch (action)
            {
                case "delete": vm.DeleteSelected(); break;
                case "edit": await vm.EditSelected(); break;
                case "key":
                    window.GetVisualDescendants().OfType<TreeViewItem>()
                        .Single(item => ReferenceEquals(item.DataContext, child)).Focus().Should().BeTrue();
                    window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
                    window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
                    break;
            }

            vm.SelectedRow.Should().BeNull();
            vm.SelectedItem.Should().BeSameAs(child);
            watches.Watches.Select(w => w.Expression).Should().ContainInOrder("count", "obj");
            await manager.DidNotReceiveWithAnyArgs().ShowDialog(default!);

            watches.Add(new WatchExpression("third", WatchType.Expression, "Module1.Go"));
            Dispatcher.UIThread.RunJobs();
            vm.SelectedItem.Should().BeNull();
            tree.SelectedItem.Should().BeNull();
        }
        finally { window.Close(); }
    }
}
