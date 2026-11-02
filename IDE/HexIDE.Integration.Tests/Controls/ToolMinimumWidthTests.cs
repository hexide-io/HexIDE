using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.AIChat.Views;

namespace HexIDE.Integration.Tests.Controls;

public class ToolMinimumWidthTests
{
    [AvaloniaTheory]
    [InlineData(false, 400)]
    [InlineData(true, 400)]
    [InlineData(false, 1100)]
    [InlineData(true, 1100)]
    public void Chat_grows_a_narrow_dock_once_and_keeps_later_resizing(bool stacked, double documentMinimum)
    {
        var chat = new ChatView(Substitute.For<IHexIdeHost>(), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        var tool = new AddinToolWindowViewModel("chat", "Chat", () => chat);
        var factory = new Factory();
        var left = new ToolDock { Proportion = LayoutManifest.DefaultLeftProportion, VisibleDockables = [tool], ActiveDockable = tool };
        IDockable column = stacked
            ? new ProportionalDock { Orientation = Orientation.Vertical, Proportion = LayoutManifest.DefaultLeftProportion, VisibleDockables = [left] }
            : left;
        var documents = new DocumentDock { MinWidth = documentMinimum, IsCollapsable = false, VisibleDockables = [] };
        var middle = new ProportionalDock { Orientation = Orientation.Vertical, IsCollapsable = false, VisibleDockables = [documents] };
        var columns = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = [column, new ProportionalDockSplitter(), middle],
        };
        var root = new RootDock { VisibleDockables = [columns], ActiveDockable = columns, DefaultDockable = columns };
        factory.InitLayout(root);
        var host = new DockControl { Layout = root };
        host.DataTemplates.Add(new ViewLocator());
        var window = new Window { Width = 1200, Height = 600, Content = host };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var input = chat.GetVisualDescendants().OfType<TextBox>().Single();
            if (documentMinimum == 400)
            {
                input.Bounds.Width.Should().BeGreaterThan(0);
                foreach (var control in chat.GetVisualDescendants().OfType<Button>().Cast<Control>().Append(input))
                {
                    var x = control.TranslatePoint(default, chat)!.Value.X;
                    (x + control.Bounds.Width).Should().BeLessThanOrEqualTo(chat.Bounds.Width + 0.01);
                }
            }
            column.Proportion.Should().BeGreaterThan(LayoutManifest.DefaultLeftProportion);
            middle.GetVisibleBounds(out _, out _, out var documentWidth, out _);
            documentWidth.Should().BeGreaterThanOrEqualTo(documentMinimum);
            if (documentMinimum == 1100) documentWidth.Should().BeApproximately(documentMinimum, 1);

            column.Proportion = 0.04;
            middle.Proportion = 0.96;
            var panel = host.GetVisualDescendants().OfType<ProportionalStackPanel>()
                .Single(p => p.Children.Any(c => ReferenceEquals(c.DataContext, column)));
            foreach (var child in panel.Children)
                if (ReferenceEquals(child.DataContext, column))
                    ProportionalStackPanel.SetProportion(child, 0.04);
                else if (ReferenceEquals(child.DataContext, middle))
                    ProportionalStackPanel.SetProportion(child, 0.96);
            window.UpdateLayout();
            var userWidth = chat.Bounds.Width;
            window.Content = null;
            window.Content = host;
            Dispatcher.UIThread.RunJobs();
            column.Proportion.Should().Be(0.04);
            chat.Bounds.Width.Should().Be(userWidth);

            var restoredTool = new AddinToolWindowViewModel("chat", "Chat", () => chat) { KeepSavedWidth = true };
            left.VisibleDockables = [restoredTool];
            left.ActiveDockable = restoredTool;
            factory.InitLayout(root);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            chat.Bounds.Width.Should().Be(userWidth, "a restored width is a user choice too");
        }
        finally { window.Close(); }
    }
}
