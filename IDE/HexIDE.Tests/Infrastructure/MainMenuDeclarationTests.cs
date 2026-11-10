using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using HexIDE.Menus;

namespace HexIDE.Tests.Infrastructure;

public class MainMenuDeclarationTests
{
    private static string Root
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "openspec")))
                directory = directory.Parent;
            return directory!.FullName;
        }
    }

    private static IEnumerable<MenuDeclaration> Walk(IEnumerable<MenuDeclaration> items) =>
        items.SelectMany(i => new[] { i }.Concat(Walk(i.Children ?? [])));

    [Fact]
    public void Main_view_has_no_handwritten_main_menu()
    {
        XDocument.Load(Path.Combine(Root, "IDE/HexIDE/MainView.axaml"))
            .Descendants().Where(e => e.Name.LocalName == "Menu").Should().BeEmpty();
        MainMenuDeclaration.Load().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("help.brandhome", "HasBrandHome")]
    [InlineData("help.brandsupport", "HasBrandSupport")]
    public void Brand_links_retain_their_visibility(string id, string visibility)
    {
        Walk(MainMenuDeclaration.Load()).Should().ContainSingle(i => i.Id == id)
            .Which.VisibilityBinding.Should().Be(visibility);
    }

    [Fact]
    public void Placements_are_unique_and_keep_the_published_ids()
    {
        var ids = Walk(MainMenuDeclaration.Load()).Select(i => i.Id).ToArray();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Equal(File.ReadAllLines(Path.Combine(Root, "IDE/HexIDE.Tests/Infrastructure/MainMenuPlacementIds.txt")));
        ids.Should().Contain(["project.references", "tools.references"]);
    }

    [Fact]
    public void Every_label_exists_in_the_canonical_pack()
    {
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "IDE/HexIDE/Localization/Packs/en.json")));
        var strings = pack.RootElement.GetProperty("strings");
        var labels = Walk(MainMenuDeclaration.Load()).Where(i => i.Kind != MenuItemKind.Separator).ToArray();
        labels.Should().NotBeEmpty();
        foreach (var item in labels)
            strings.TryGetProperty(item.LabelKey!, out _).Should().BeTrue(item.Id);
    }

    [Fact]
    public void Every_registered_command_names_a_real_host_command_or_binding_method()
    {
        MenuHostRegistry.Commands.Should().NotBeEmpty();
        foreach (var command in MenuHostRegistry.Commands.Values)
        {
            if (command.RoutedCommand is not null) continue;
            var members = typeof(MainViewViewModel).GetMember(command.ShellMember!, BindingFlags.Public | BindingFlags.Instance);
            members.Should().ContainSingle();
            var member = members.Single();
            (member is PropertyInfo p && typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType)
                || member is MethodInfo m && m.GetParameters().Length == 0).Should().BeTrue(command.ShellMember);
        }
    }

    [Theory]
    [InlineData("toolbar.standard.visible", "IsStandardToolbarVisible")]
    [InlineData("toolbar.edit.visible", "IsEditToolbarVisible")]
    [InlineData("toolbar.debug.visible", "IsDebugToolbarVisible")]
    [InlineData("toolbar.form-editor.visible", "IsFormEditorToolbarVisible")]
    [InlineData("editor.minimap.visible", "IsMinimapVisible")]
    public void Toggle_getter_and_setter_use_the_existing_setting(string name, string property)
    {
        var directory = Path.Combine(Root, "artifacts", "menu-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new HexIDE.IDE.SettingsService(Path.Combine(directory, "settings.json"));
            var state = MenuHostRegistry.States[name];
            var setting = typeof(HexIDE.IDE.SettingsService).GetProperty(property)!;
            foreach (var value in new[] { false, true })
            {
                state.Set(settings, value);
                setting.GetValue(settings).Should().Be(value);
                setting.SetValue(settings, !value);
                state.Get(settings).Should().Be(!value);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("{\"kind\":\"Command\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\",\"command\":\"missing\"}")]
    [InlineData("{\"kind\":\"Provider\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\",\"provider\":\"missing\"}")]
    [InlineData("{\"kind\":\"Toggle\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\",\"state\":\"missing\"}")]
    [InlineData("{\"kind\":\"Placeholder\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\",\"children\":[]}")]
    [InlineData("{\"kind\":\"Placeholder\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\",\"command\":\"application.AboutCommand\"}")]
    [InlineData("{\"kind\":\"Submenu\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\"}")]
    [InlineData("{\"kind\":\"Placeholder\",\"id\":\"test\"}")]
    [InlineData("{\"kind\":\"Separator\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\"}")]
    [InlineData("{\"kind\":\"Placeholder\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\",\"labelBinding\":\"missing\"}")]
    [InlineData("{\"kind\":\"Command\",\"id\":\"test\",\"labelKey\":\"Str.Menu.File\",\"command\":\"application.AboutCommand\",\"arguments\":{\"value\":{\"kind\":\"Boolean\",\"value\":true}}}")]
    [InlineData("{\"kind\":\"Separator\",\"id\":\"test\",\"visibility\":\"missing\"}")]
    public void Invalid_item_is_refused_with_source_and_placement(string item)
    {
        var parse = () => MainMenuDeclaration.Parse($"[{item}]", "test-menu.json");
        var error = parse.Should().Throw<MenuDeclarationException>().Which;
        error.SourceName.Should().Be("test-menu.json");
        error.PlacementId.Should().Be("test");
    }

    [Theory]
    [InlineData((int)MenuArgumentKind.String, "\"text\"")]
    [InlineData((int)MenuArgumentKind.Boolean, "true")]
    [InlineData((int)MenuArgumentKind.Integer, "42")]
    public void Typed_arguments_match_the_registered_signature(int kindValue, string json)
    {
        var kind = (MenuArgumentKind)kindValue;
        using var value = JsonDocument.Parse(json);
        var item = new MenuDeclaration
        {
            Kind = MenuItemKind.Command, Id = "test", LabelKey = "Str.Menu.File", Command = "typed",
            Arguments = new() { ["value"] = new(kind, value.RootElement.Clone()) }
        };
        var registry = new Dictionary<string, MenuCommandRegistration>
        {
            ["typed"] = new("test", null, null) { Arguments = new Dictionary<string, MenuArgumentKind> { ["value"] = kind } }
        };
        var validate = () => MainMenuDeclaration.Validate([item], "typed.json", registry);
        validate.Should().NotThrow();
        var wrong = item with { Arguments = new() { ["value"] = new(kind, JsonSerializer.SerializeToElement(new[] { 1 })) } };
        var invalid = () => MainMenuDeclaration.Validate([wrong], "typed.json", registry);
        invalid.Should().Throw<MenuDeclarationException>().Which.PlacementId.Should().Be("test");
    }

    [Fact]
    public void Duplicate_nested_placement_is_attributed()
    {
        var parse = () => MainMenuDeclaration.Parse("""
            [{"kind":"Submenu","id":"test","labelKey":"Str.Menu.File",
              "children":[{"kind":"Separator","id":"test"}]}]
            """, "duplicate.json");
        parse.Should().Throw<MenuDeclarationException>().Which.PlacementId.Should().Be("test");
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"kind\":\"unknown\",\"id\":\"test\"}]")]
    [InlineData("[{\"kind\":\"Placeholder\",\"id\":\"test\",\"typo\":true}]")]
    public void Malformed_declarations_are_attributed(string json)
    {
        var parse = () => MainMenuDeclaration.Parse(json, "broken.json");
        parse.Should().Throw<MenuDeclarationException>().Which.SourceName.Should().Be("broken.json");
    }
}
