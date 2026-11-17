using System.Linq;
using System.Threading.Tasks;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class ToolPaneMenuTests
{
    [AvaloniaFact]
    public async Task Immediate_title_bar_opens_its_menu_and_collapse_removes_the_overlay()
    {
        await using var owned = new RealComposition();
        var window = ToolbarAutomationNameTests.ShowMainView();
        var app = new App();
        app.LoadMarkup();
        var resources = app.Resources;
        app.Resources = new ResourceDictionary();
        window.Resources.MergedDictionaries.Add(resources);
        while (app.Styles.Count > 0)
        {
            var style = app.Styles[0];
            app.Styles.RemoveAt(0);
            window.Styles.Add(style);
        }
        window.DataTemplates.Add(new ViewLocator());
        ((MainView)window.Content!).DataContext = owned.Composition.Root;
        var immediate = owned.Composition.Root.Immediate;
        var factory = new Dock.Model.Mvvm.Factory();
        var tools = new Dock.Model.Mvvm.Controls.ToolDock
        {
            ActiveDockable = immediate, VisibleDockables = [immediate],
        };
        var root = new Dock.Model.Mvvm.Controls.RootDock
        {
            Id = "Root dock", ActiveDockable = tools, DefaultDockable = tools, VisibleDockables = [tools],
        };
        factory.InitLayout(root);
        ((MainView)window.Content!).FindControl<DockControl>("DockControl")!.Layout = root;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            var chrome = window.GetVisualDescendants().OfType<ToolChromeControl>()
                .Single(c => ReferenceEquals(c.DataContext, owned.Composition.Root.Immediate.Owner));
            var border = chrome.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "PART_Border" && ReferenceEquals(c.TemplatedParent, chrome));
            var flyout = (PopupFlyoutBase)border.ContextFlyout!;
            var peer = ControlAutomationPeer.CreatePeerForElement(chrome);
            var provider = peer.GetProvider<IExpandCollapseProvider>();
            var path = UiAutomationDriver.PathWithin(window, "Window", chrome)!;
            path.Should().EndWith("TitleBar[Tool.immediate]");
            var addressed = UiAutomationDriver.Resolve(window, path).control!;
            addressed.Should().BeSameAs(chrome);
            var opened = await UiAutomationDriver.InteractAsync(addressed, "expand", null);
            opened.Success.Should().BeTrue();
            opened.Detail.Should().Contain("pane menu on PART_Border",
                $"peer {peer.GetType().FullName}, provider {provider?.GetType().FullName}, state {provider?.ExpandCollapseState}, "
                + $"flyout open {flyout.IsOpen}, target {flyout.Target?.GetType().Name}, popup child {flyout.Popup?.Child?.GetType().Name}, "
                + $"overlay {window.GetVisualDescendants().Count(c => c.GetType().Name == "LightDismissOverlayLayer")}");
            flyout.IsOpen.Should().BeTrue($"peer {peer.GetType().Name}, provider {provider?.GetType().Name}, reply {opened}");
            var tree = UiAutomationDriver.Dump(window, "Window", 30, false);
            var dump = System.Text.Json.JsonSerializer.Serialize(tree);
            dump.Should().Contain("Float").And.Contain("Dock as Tabbed Document").And.Contain("Close");
            var closed = await UiAutomationDriver.InteractAsync(chrome, "collapse", null);
            closed.Success.Should().BeTrue();
            flyout.IsOpen.Should().BeFalse();
            (await UiAutomationDriver.InteractAsync(chrome, "collapse", null)).Detail.Should().Contain("nothing to close");
            var title = chrome.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "PART_Title");
            var pressed = await UiAutomationDriver.InteractAsync(title, "click", "10,5 right");
            var click = await UiAutomationDriver.FinishAsync(pressed);
            click.Success.Should().BeTrue();
            click.Mechanism.Should().Be("pointer");
            click.Detail.Should().Contain("TextBlock[PART_Title]").And.NotContain("LightDismissOverlayLayer");
            flyout.IsOpen.Should().BeTrue("the completed right-click opens the pane menu");
            flyout.Hide();
            (await UiAutomationDriver.InteractAsync(border, "expand", null)).Success.Should().BeTrue();
            flyout.IsOpen.Should().BeTrue();
            flyout.Hide();
        }
        finally { window.Close(); }
    }
}
