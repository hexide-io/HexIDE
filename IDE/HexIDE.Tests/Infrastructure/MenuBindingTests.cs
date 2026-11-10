using System.Xml.Linq;
using HexIDE.Menus;

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

    private static IEnumerable<MenuDeclaration> Walk(IEnumerable<MenuDeclaration> items) =>
        items.SelectMany(i => new[] { i }.Concat(Walk(i.Children ?? [])));

    private static MenuDeclaration MenuItem(string key) =>
        Walk(MainMenuDeclaration.Load()).Single(i => i.LabelKey == key);

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

    [Theory]
    [InlineData("Str.Menu.File.Print")]
    [InlineData("Str.Menu.File.PrintSetup")]
    [InlineData("Str.Menu.Tools.ProcedureAttributes")]
    public void Unimplemented_printing_and_procedure_attributes_are_disabled(string key)
    {
        MenuItem(key).Command.Should().Be("application.DisabledCommand");
        ApplicationCommands.DisabledCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ReopenClosedWindowUsesTheRebindableShellCommandAndListsHistory()
    {
        var view = XDocument.Load(Path.Combine(ViewsDirectory(), "MainView.axaml"));
        var command = "{x:Static vb:ApplicationCommands.ReopenClosedDocumentCommand}";
        MenuItem("Str.Menu.Window.ReopenClosed").Command.Should().Be("application.ReopenClosedDocumentCommand");
        view.Descendants().Single(e => e.Name.LocalName == "CommandBinding" &&
                (string?)e.Attribute("Command") == command)
            .Attribute("ExecutingCommand")!.Value.Should().Be("{CompiledBinding ReopenClosedDocumentCommand}");
        MenuItem("Str.Menu.Window.RecentlyClosed").Provider.Should().Be("closed-documents");
    }

    [Fact]
    public void AddMdiForm_uses_the_disabled_command_like_its_toolbar_twin()
    {
        MenuItem("Str.Menu.Project.AddMDIForm").Command.Should().Be("application.DisabledCommand");
    }

    [Fact]
    public void ToolbarFlyoutListsTheSameFourTogglesAsTheViewMenu()
    {
        var view = XDocument.Load(Path.Combine(ViewsDirectory(), "MainView.axaml"));
        var flyout = view.Descendants().Single(e => e.Name.LocalName == "StackPanel.ContextFlyout")
            .Elements().Single();
        var toggles = flyout.Elements().TakeWhile(e => e.Name.LocalName != "Separator").ToArray();
        var menu = MenuItem("Str.Menu.View.Toolbars").Children!;
        toggles.Select(e => (string?)e.Attribute("Header")).Should().Equal(
            menu.Select(i => "{DynamicResource " + i.LabelKey + "}"));
        toggles.Should().HaveCount(4);
        foreach (var (toggle, index) in toggles.Select((item, index) => (item, index)))
        {
            var counterpart = menu[index];
            counterpart.Kind.Should().Be(MenuItemKind.Toggle);
            toggle.Attribute("ToggleType")!.Value.Should().Be("CheckBox");
            string.Concat(toggle.Attribute("IsChecked")!.Value.Where(c => !char.IsWhiteSpace(c)))
                .Should().Be("{CompiledBindingSettings." + MenuHostRegistry.States[counterpart.State!].SettingsProperty + ",Mode=TwoWay}");
        }
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
                // Align to Grid remains unimplemented.
                if (path == "VisualDesigner/Views/FormEditView.axaml" && header == "{DynamicResource Str.Designer.Context.AlignToGrid}")
                    continue;
                inert.Add($"{path}: {header}");
            }
        }
        inert.Should().BeEmpty("enabled leaf menu items must respond to being chosen");
    }
}
