using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using HexIDE.Automation;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Automation;

public class ToolPathTests
{
    [AvaloniaTheory]
    [InlineData("locals", "Str.Tool.Locals.Title")]
    [InlineData("properties", "Str.Tool.Properties.Title")]
    [InlineData("project", "Str.Tool.Project.TitleEmpty")]
    public void Tool_parts_keep_their_paths_in_German(string key, string titleKey)
    {
        var localization = new LocalizationService();
        var layout = DragDockTests.Shown((alpha, _) => Register(alpha, key));
        try
        {
            // The same identity the factory registers for the saved layout.
            layout.Alpha.Title = "English title";
            Dispatcher.UIThread.RunJobs();
            var chrome = DragDockTests.ChromeOf(layout, layout.Left);
            var pane = chrome.GetVisualDescendants().OfType<ToolControl>().Single();
            var chromePath = UiAutomationDriver.PathWithin(layout.Window, "Window", chrome)!;
            var panePath = UiAutomationDriver.PathWithin(layout.Window, "Window", pane)!;
            chromePath.Should().Contain("TitleBar[Tool." + key + "]");
            panePath.Should().Contain("Pane[Tool." + key + "]");
            localization.Apply("de");
            layout.Alpha.Title = localization.GetString(titleKey) + (key == "project" ? " - OtherProject" : "");
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Resolve(layout.Window, chromePath).control.Should().BeSameAs(chrome);
            UiAutomationDriver.Resolve(layout.Window, panePath).control.Should().BeSameAs(pane);
            ControlAutomationPeer.CreatePeerForElement(chrome).GetName().Should().Be(layout.Alpha.Title);
        }
        finally { layout.Window.Close(); localization.Apply("en"); }
    }

    [AvaloniaFact]
    public void Document_region_tool_tab_has_a_stable_key_in_German()
    {
        var localization = new LocalizationService();
        var tool = new Document { Title = "Language & Debug Servers" };
        Register(tool, "languageServers");
        var tab = new DocumentTabStripItem { DataContext = tool };
        var window = new Window { Content = tab };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var path = UiAutomationDriver.PathWithin(window, "Window", tab)!;
            path.Should().Contain("TabItem[Tool.languageServers]");
            localization.Apply("de");
            tool.Title = localization.GetString("Str.Tool.LanguageServers.Title");
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Resolve(window, path).control.Should().BeSameAs(tab);
        }
        finally { window.Close(); localization.Apply("en"); }
    }

    // Reflection lets the regression run against the unmodified product before the helper exists.
    private static void Register(Dock.Model.Core.IDockable tool, string key) =>
        typeof(UiAutomationDriver).Assembly.GetType("HexIDE.Automation.ToolAutomation")?
            .GetMethod("Register")?.Invoke(null, [tool, key]);

    [AvaloniaFact]
    public void A_tab_groups_chrome_does_not_change_when_another_tool_is_selected()
    {
        var layout = DragDockTests.Shown((alpha, beta) => { Register(alpha, "locals"); Register(beta, "watches"); });
        try
        {
            layout.Right.VisibleDockables!.Remove(layout.Beta);
            layout.Left.VisibleDockables!.Add(layout.Beta);
            Dispatcher.UIThread.RunJobs();
            var chrome = DragDockTests.ChromeOf(layout, layout.Left);
            var path = UiAutomationDriver.PathWithin(layout.Window, "Window", chrome)!;
            path.Should().Contain("TitleBar[ToolGroup.locals+watches]");
            layout.Left.ActiveDockable = layout.Beta;
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.PathWithin(layout.Window, "Window", chrome).Should().Be(path);
            UiAutomationDriver.Resolve(layout.Window, path).control.Should().BeSameAs(chrome);
        }
        finally { layout.Window.Close(); }
    }
}
