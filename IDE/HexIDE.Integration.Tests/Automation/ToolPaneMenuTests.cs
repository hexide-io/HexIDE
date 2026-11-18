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
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Automation;

public class ToolPaneMenuTests
{
    [AvaloniaFact]
    public async Task Immediate_title_bar_opens_its_menu_and_collapse_removes_the_overlay()
    {
        // The profile is shared by every test in the process (TemporaryProfile), and a composition builds its layout from the
        // profile's saved dock-layout.json. One saved after a test opened the Immediate pane lists it, which is the state
        // #4521 failed in, so start from that state rather than from whatever ran before.
        using var layout = new ImmediateOpenLayout();
        await using var owned = new RealComposition();
        // DISetup's Root is transient: every read builds a new MainViewViewModel, whose layout adopts the singleton panes
        // it lists, the Immediate pane among them. So read it once, and find the chrome by the dock built here (#4521).
        var main = owned.Composition.Root;
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
        ((MainView)window.Content!).DataContext = main;
        var immediate = main.Immediate;
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
                .Single(c => ReferenceEquals(c.DataContext, tools));
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

    /// <summary>Saves a layout that lists the Immediate pane as open, and puts back what was saved before.</summary>
    private sealed class ImmediateOpenLayout : IDisposable
    {
        private readonly string path = UserDataPath.For("dock-layout.json");
        private readonly string? saved;

        public ImmediateOpenLayout()
        {
            saved = File.Exists(path) ? File.ReadAllText(path) : null;
            File.WriteAllText(path, LayoutManifestJson.Serialize(LayoutManifest.Default with
            {
                Tools = LayoutManifest.Default.Tools.Select(t => t.Key == "immediate" ? t with { Open = true } : t).ToArray(),
            }));
        }

        public void Dispose()
        {
            if (saved is null) File.Delete(path);
            else File.WriteAllText(path, saved);
        }
    }
}
