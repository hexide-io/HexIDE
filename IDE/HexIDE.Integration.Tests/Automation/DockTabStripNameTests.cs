using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class DockTabStripNameTests
{
    private static IEnumerable<UiNode> Flatten(UiNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    [AvaloniaTheory]
    [InlineData("Document", false, "Document tabs")]
    [InlineData("Tool", false, "Tool tabs")]
    [InlineData("Horizontal", false, "Document tabs")]
    [InlineData("Vertical", false, "Document tabs")]
    [InlineData("Document", true, "Dokumentregisterkarten")]
    [InlineData("Tool", true, "Werkzeugregisterkarten")]
    [InlineData("Horizontal", true, "Dokumentregisterkarten")]
    [InlineData("Vertical", true, "Dokumentregisterkarten")]
    public void Tab_strips_have_localized_automation_names(string kind, bool german, string expected)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Dock.DocumentTabs").Returns(german ? "Dokumentregisterkarten" : "Document tabs");
        localization.GetString("Str.Dock.ToolTabs").Returns(german ? "Werkzeugregisterkarten" : "Tool tabs");
        var factory = new Factory();
        var first = new Document { Title = "First" };
        var second = new Document { Title = "Second" };
        var documents = new DocumentDock { VisibleDockables = [first, second], ActiveDockable = first };
        var tools = new ToolDock { VisibleDockables = [new Tool { Title = "Tool" }] };
        tools.ActiveDockable = tools.VisibleDockables[0];
        var root = new RootDock
        {
            VisibleDockables = [new ProportionalDock { VisibleDockables = [tools, documents] }],
        };
        root.ActiveDockable = root.VisibleDockables[0];
        factory.InitLayout(root);
        if (kind == "Horizontal") factory.NewHorizontalDocumentDock(second);
        if (kind == "Vertical") factory.NewVerticalDocumentDock(second);
        var host = new DockControl { Layout = root };
        var window = new Window { Content = host, Width = 800, Height = 600 };
        foreach (var key in new[] { "Str.Dock.DocumentTabs", "Str.Dock.ToolTabs" })
            window.Resources[key] = localization.GetString(key);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            if (kind == "Tool")
            {
                var strip = host.GetVisualDescendants().OfType<ToolTabStrip>().Single();
                ControlAutomationPeer.CreatePeerForElement(strip)!.GetName().Should().Be(expected);
            }
            else
            {
                var strips = host.GetVisualDescendants().OfType<DocumentTabStrip>().ToArray();
                strips.Should().HaveCount(kind == "Document" ? 1 : 2);
                foreach (var strip in strips)
                    ControlAutomationPeer.CreatePeerForElement(strip)!.GetName().Should().Be(expected);
                if (kind is "Horizontal" or "Vertical")
                    second.Owner!.Title.Should().Be(nameof(Dock.Model.Controls.IDocumentDock));
            }
            var nodes = Flatten(UiAutomationDriver.Dump(window, "Window", 40, false))
                .Where(n => n.ClassName is nameof(DocumentTabStrip) or nameof(ToolTabStrip)).ToArray();
            nodes.Should().HaveCount(kind is "Horizontal" or "Vertical" ? 3 : 2);
            foreach (var node in nodes)
            {
                node.Path.Should().EndWith("/Tab[PART_TabStrip]");
                UiAutomationDriver.Resolve(window, node.Path).control.Should().NotBeNull();
            }
        }
        finally { window.Close(); }
    }
}
