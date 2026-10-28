using System.Xml.Linq;

namespace HexIDE.Tests.Infrastructure;

public class MenuBindingTests
{
    private static string ViewsDirectory()
    {
        DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "openspec")))
            root = root.Parent;
        root.Should().NotBeNull();
        return Path.Combine(root!.FullName, "IDE", "HexIDE");
    }

    [Theory]
    [InlineData("Standard.End")]
    [InlineData("Debug.End")]
    public void EndToolbarUsesTheCommandIndependentOfWindowFocus(string automationId)
    {
        var button = XDocument.Load(Path.Combine(ViewsDirectory(), "MainView.axaml"))
            .Descendants().Single(e => e.Name.LocalName == "Button" &&
                (string?)e.Attribute("AutomationProperties.AutomationId") == automationId);
        button.Attribute("Command")!.Value.Should().Be("{CompiledBinding EndProjectCommand}");
    }

    [Fact]
    public void ReopenClosedWindowUsesTheRebindableShellCommandAndListsHistory()
    {
        var view = XDocument.Load(Path.Combine(ViewsDirectory(), "MainView.axaml"));
        var command = "{x:Static vb:ApplicationCommands.ReopenClosedDocumentCommand}";
        view.Descendants().Single(e => e.Name.LocalName == "MenuItem" &&
                (string?)e.Attribute("Header") == "{DynamicResource Str.Menu.Window.ReopenClosed}")
            .Attribute("Command")!.Value.Should().Be(command);
        view.Descendants().Single(e => e.Name.LocalName == "CommandBinding" &&
                (string?)e.Attribute("Command") == command)
            .Attribute("ExecutingCommand")!.Value.Should().Be("{CompiledBinding ReopenClosedDocumentCommand}");
        view.Descendants().Single(e => e.Name.LocalName == "MenuItem" &&
                (string?)e.Attribute("Header") == "{DynamicResource Str.Menu.Window.RecentlyClosed}")
            .Attribute("ItemsSource")!.Value.Should().Be("{CompiledBinding ClosedDocumentMenuItems}");
    }

    [Fact]
    public void AddMdiForm_uses_the_disabled_command_like_its_toolbar_twin()
    {
        var items = XDocument.Load(Path.Combine(ViewsDirectory(), "MainView.axaml"))
            .Descendants().Where(e => e.Name.LocalName == "MenuItem" &&
                (string?)e.Attribute("Header") == "{DynamicResource Str.Menu.Project.AddMDIForm}");
        ((string?)items.Single().Attribute("Command")).Should()
            .Be("{x:Static vb:ApplicationCommands.DisabledCommand}");
    }

    [Fact]
    public void Axaml_leaf_menu_items_have_an_action_or_are_disabled()
    {
        var views = ViewsDirectory();
        var inert = new List<string>();
        var ide = Path.GetDirectoryName(views)!;
        foreach (var file in Directory.EnumerateFiles(ide, "*.axaml", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(ide, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                continue;
            foreach (var item in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "MenuItem"))
            {
                if (item.Elements().Any(e => e.Name.LocalName == "MenuItem") ||
                    item.Attribute("ItemsSource") != null ||
                    item.Attribute("Command") != null || item.Attribute("Click") != null ||
                    item.Attribute("IsChecked") != null ||
                    string.Equals((string?)item.Attribute("IsEnabled"), "False", StringComparison.OrdinalIgnoreCase))
                    continue;
                var path = Path.GetRelativePath(views, file).Replace(Path.DirectorySeparatorChar, '/');
                var header = (string?)item.Attribute("Header");
                // Filled from code in MainView.axaml.cs.
                if (path == "MainView.axaml" && (string?)item.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "AddInsMenuItem")
                    continue;
                // Align to Grid remains unimplemented.
                if (path == "VisualDesigner/Views/FormEditView.axaml" && header == "{DynamicResource Str.Designer.Context.AlignToGrid}")
                    continue;
                inert.Add($"{path}: {header}");
            }
        }
        inert.Should().BeEmpty("enabled leaf menu items must respond to being chosen");
    }
}
