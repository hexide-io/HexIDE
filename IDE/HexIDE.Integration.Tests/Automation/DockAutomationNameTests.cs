using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Commands;
using Dock.Model.Mvvm.Controls;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class DockAutomationNameTests
{
    private static void AssertName(Window window, Button button, string expected)
    {
        ControlAutomationPeer.CreatePeerForElement(button).GetName().Should().Be(expected);
        var path = UiAutomationDriver.PathWithin(window, "Window", button)!;
        path.Should().NotBeNull();
        var (resolved, error) = UiAutomationDriver.Resolve(window, path);
        error.Should().BeNull();
        resolved.Should().BeSameAs(button);
        var labelPath = path[..(path.LastIndexOf('/') + 1)] + $"Button[{expected}]";
        var (byLabel, labelError) = UiAutomationDriver.Resolve(window, labelPath);
        labelError.Should().BeNull("the peer's name must be accepted as well as the template part's name");
        byLabel.Should().BeSameAs(button);
    }

    [AvaloniaTheory]
    [InlineData("PART_MenuButton", "Pane menu")]
    [InlineData("PART_PinButton", "Pin or unpin pane")]
    [InlineData("PART_MaximizeRestoreButton", "Maximize or restore pane")]
    [InlineData("PART_CloseButton", "Close pane")]
    public void Tool_chrome_buttons_have_usable_names(string part, string expected)
    {
        var tool = new Tool { Title = "Properties", CanClose = true, CanPin = true };
        var chrome = new ToolChromeControl
        {
            DataContext = new ToolDock { ActiveDockable = tool, VisibleDockables = [tool] },
        };
        var window = ToolbarAutomationNameTests.ShowMainView();
        try
        {
            window.Content = chrome;
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            var button = chrome.GetVisualDescendants().OfType<Button>().Single(b => b.Name == part);
            AssertName(window, button, expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("DockTabStripScrollViewerHorizontal", ScrollViewerLineDirection.Left, "Scroll tabs left")]
    [InlineData("DockTabStripScrollViewerHorizontal", ScrollViewerLineDirection.Right, "Scroll tabs right")]
    [InlineData("DockTabStripScrollViewerHorizontalOverlay", ScrollViewerLineDirection.Left, "Scroll tabs left")]
    [InlineData("DockTabStripScrollViewerHorizontalOverlay", ScrollViewerLineDirection.Right, "Scroll tabs right")]
    [InlineData("DockTabStripScrollViewerHorizontal", ScrollViewerLineDirection.Up, "Scroll tabs up")]
    [InlineData("DockTabStripScrollViewerHorizontal", ScrollViewerLineDirection.Down, "Scroll tabs down")]
    [InlineData("DockTabStripScrollViewerHorizontalOverlay", ScrollViewerLineDirection.Up, "Scroll tabs up")]
    [InlineData("DockTabStripScrollViewerHorizontalOverlay", ScrollViewerLineDirection.Down, "Scroll tabs down")]
    public void Tab_scroll_buttons_have_usable_names(string theme, ScrollViewerLineDirection direction, string expected)
    {
        var window = ToolbarAutomationNameTests.ShowMainView();
        try
        {
            var scroll = new ScrollViewer
            {
                Name = "PART_ScrollViewer",
                Theme = (ControlTheme)window.FindResource(theme)!,
                Content = new Border { Width = 3000, Height = 30 },
            };
            window.Content = scroll;
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            var buttons = scroll.GetVisualDescendants().OfType<RepeatButton>().ToList();
            buttons.Should().HaveCount(4, "the Dock template must actually have been applied");
            var button = buttons.Single(b => b.Command is ScrollViewerLineCommand command && command.Direction == direction);
            AssertName(window, button, expected);
        }
        finally { window.Close(); }
    }
}
