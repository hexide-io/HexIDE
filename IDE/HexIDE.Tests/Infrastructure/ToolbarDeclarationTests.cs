using System.Text.Json;
using System.Xml.Linq;
using HexIDE.Menus;

namespace HexIDE.Tests.Infrastructure;

public class ToolbarDeclarationTests
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

    private static XDocument View() => XDocument.Load(Path.Combine(Root, "IDE/HexIDE/MainView.axaml"));
    private static string? Attribute(XElement element, string name) => (string?)element.Attribute(name);
    private static string Resource(string key) => "{DynamicResource " + key + "}";
    private static string Binding(string key) => "{CompiledBinding " + key + "}";
    private static IEnumerable<MenuDeclaration> Walk(IEnumerable<MenuDeclaration> items) =>
        items.SelectMany(i => new[] { i }.Concat(Walk(i.Children ?? [])));

    [Fact]
    public void Placements_keep_the_published_unique_ids()
    {
        var ids = ToolbarDeclaration.Load().SelectMany(b => new[] { b.Id }.Concat(b.Items.SelectMany(i =>
            new[] { i.Id }.Concat(Walk(i.Children ?? []).Select(c => c.Id))))).ToArray();
        ids.Should().NotBeEmpty();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Equal(File.ReadAllLines(Path.Combine(Root, "IDE/HexIDE.Tests/Infrastructure/ToolbarPlacementIds.txt")));
    }

    [Fact]
    public void Interactive_placements_have_existing_localized_tooltips_and_automation_names()
    {
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "IDE/HexIDE/Localization/Packs/en.json")));
        var strings = pack.RootElement.GetProperty("strings");
        var items = ToolbarDeclaration.Load().SelectMany(b => b.Items).Where(i => i.TooltipKey is not null).ToArray();
        items.Should().NotBeEmpty();
        foreach (var item in items)
        {
            strings.TryGetProperty(item.TooltipKey!, out _).Should().BeTrue(item.Id);
            strings.TryGetProperty(item.AutomationNameKey!, out _).Should().BeTrue(item.Id);
        }
        foreach (var child in ToolbarDeclaration.Load().SelectMany(b => b.Items).SelectMany(i => Walk(i.Children ?? [])))
            if (child.LabelKey is not null) strings.TryGetProperty(child.LabelKey, out _).Should().BeTrue(child.Id);
    }

    [Fact]
    public void Declaration_matches_the_real_axaml_toolbars()
    {
        CheckParity(View(), ToolbarDeclaration.Load());
    }

    private static void CheckParity(XDocument view, ToolbarBand[] bands)
    {
        bands.Should().HaveCount(4);
        var hosts = view.Descendants().Where(e => Attribute(e, "Classes") == "toolbar").ToArray();
        hosts.Should().HaveCount(bands.Length);
        for (var b = 0; b < bands.Length; b++)
        {
            var band = bands[b];
            var host = hosts[b];
            Attribute(host, "AutomationProperties.AutomationId").Should().Be(band.AutomationId);
            Attribute(host, "IsVisible").Should().Be(Binding(band.VisibilityBinding));
            var elements = host.Elements().Single().Elements().ToArray();
            var index = 0;
            foreach (var item in band.Items)
            {
                var element = elements[index++];
                if (item.Kind == ToolbarItemKind.Provider)
                {
                    item.Provider.Should().Be("selection-readout");
                    item.VisibilityBinding.Should().BeNull(item.Id);
                    // #3621's host-owned run includes both icons, both labels and both spacers.
                    var expected = XElement.Parse("""
                        <StackPanel xmlns="https://github.com/avaloniaui">
                          <Path Data="{StaticResource Geo.Position}" Fill="{DynamicResource IconInk}" Width="16" Height="16" Stretch="Uniform" />
                          <TextBlock VerticalAlignment="Center" Text="{CompiledBinding FocusedProjectUtil.FocusedComponentPosition}" />
                          <Border Width="50" />
                          <Path Data="{StaticResource Geo.Size}" Fill="{DynamicResource IconInk}" Width="16" Height="16" Stretch="Uniform" />
                          <TextBlock VerticalAlignment="Center" Text="{CompiledBinding FocusedProjectUtil.FocusedComponentSize}" />
                          <Border Width="20" />
                        </StackPanel>
                        """);
                    var actual = elements.Skip(index - 1).Take(6).ToArray();
                    actual.Should().HaveCount(6);
                    actual.Zip(expected.Elements(), XNode.DeepEquals).Should().OnlyContain(equal => equal, item.Id);
                    index += 5;
                    continue;
                }
                element.Name.LocalName.Should().Be(item.Kind switch
                {
                    ToolbarItemKind.Separator => "Separator",
                    ToolbarItemKind.Toggle => "ToggleButton",
                    _ => "Button"
                }, item.Id);
                Attribute(element, "IsVisible").Should().Be(item.VisibilityBinding is null ? null : Binding(item.VisibilityBinding));
                Attribute(element, "Command").Should().Be(item.Command is null ? null : MenuHostRegistry.Commands[item.Command].AxamlBinding);
                Attribute(element, "CommandParameter").Should().BeNull();
                Attribute(element, "IsChecked").Should().Be(item.CheckedBinding);
                Attribute(element, "ToolTip.Tip").Should().Be(item.TooltipKey is null ? null : Resource(item.TooltipKey));
                Attribute(element, "AutomationProperties.AutomationId").Should().Be(item.AutomationId);
                // MainView's shared style derives the name from the tooltip.
                item.AutomationNameKey.Should().Be(item.TooltipKey);
                Attribute(element, "AutomationProperties.Name").Should().BeNull();
                var flyouts = element.Elements().Where(e => e.Name.LocalName == "Button.Flyout").ToArray();
                flyouts.Should().HaveCount(item.Kind == ToolbarItemKind.Flyout ? 1 : 0);
                if (item.Kind != ToolbarItemKind.Flyout) continue;
                var menu = flyouts.Single().Elements().Single();
                menu.Name.LocalName.Should().Be("MenuFlyout");
                Attribute(menu, "Opening").Should().Be(item.Provider is null ? null : MenuHostRegistry.ToolbarProviders[item.Provider]);
                var children = menu.Elements().ToArray();
                children.Should().HaveCount(item.Children?.Length ?? 0);
                for (var c = 0; c < children.Length; c++)
                {
                    var child = item.Children![c];
                    children[c].Elements().Should().BeEmpty(child.Id);
                    children[c].Name.LocalName.Should().Be(child.Kind == MenuItemKind.Separator ? "Separator" : "MenuItem");
                    Attribute(children[c], "Header").Should().Be(child.LabelKey is null ? null : Resource(child.LabelKey));
                    Attribute(children[c], "Command").Should().Be(child.Command is null ? null : MenuHostRegistry.Commands[child.Command].AxamlBinding);
                    Attribute(children[c], "IsVisible").Should().Be(child.VisibilityBinding is null ? null : Binding(child.VisibilityBinding));
                    Attribute(children[c], "CommandParameter").Should().BeNull();
                }
            }
            index.Should().Be(elements.Length, band.Id);
        }
        var style = view.Descendants().Single(e => e.Name.LocalName == "Style"
            && Attribute(e, "Selector") == "ClassicBorderDecorator.toolbar :is(Button)");
        style.Elements().Should().ContainSingle(e => Attribute(e, "Property") == "AutomationProperties.Name"
            && Attribute(e, "Value") == "{Binding $self.(ToolTip.Tip)}");
    }

    [Theory]
    [InlineData("command", "missing")]
    [InlineData("provider", "missing")]
    [InlineData("checkedBinding", "missing")]
    [InlineData("visibilityBinding", "missing")]
    [InlineData("tooltipKey", "not-localized")]
    [InlineData("automationNameKey", "not-localized")]
    [InlineData("automationId", "")]
    public void Invalid_placement_is_refused_with_source_and_id(string property, string value)
    {
        var item = new ToolbarPlacement
        {
            Kind = ToolbarItemKind.Toggle, Id = "test", Command = "application.LockControlsCommand",
            TooltipKey = "Str.Toolbar.Tip.LockControls", AutomationNameKey = "Str.Toolbar.Tip.LockControls", AutomationId = "test",
            CheckedBinding = MenuHostRegistry.CheckedBindings.Single()
        };
        var json = JsonSerializer.SerializeToNode(item, ToolbarJsonContext.Default.ToolbarPlacement)!;
        json[property] = value;
        var parse = () => ToolbarDeclaration.Parse("[{\"id\":\"band\",\"automationId\":\"Band\",\"visibilityBinding\":\"Settings.IsStandardToolbarVisible\",\"items\":[" + json.ToJsonString() + "]}]", "bad.json");
        var error = parse.Should().Throw<MenuDeclarationException>().Which;
        error.SourceName.Should().Be("bad.json");
        error.PlacementId.Should().Be("test");
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"id\":\"band\",\"automationId\":\"Band\",\"visibilityBinding\":\"missing\",\"items\":[]}]")]
    [InlineData("[{\"id\":\"band\",\"automationId\":\"Band\",\"visibilityBinding\":\"Settings.IsStandardToolbarVisible\",\"items\":[null]}]")]
    [InlineData("[{\"id\":\"band\",\"automationId\":\"Band\",\"visibilityBinding\":\"Settings.IsStandardToolbarVisible\",\"items\":[{\"kind\":\"Separator\",\"id\":\"test\",\"typo\":true}]}]")]
    public void Malformed_declaration_is_attributed(string json)
    {
        var parse = () => ToolbarDeclaration.Parse(json, "malformed.json");
        parse.Should().Throw<MenuDeclarationException>().Which.SourceName.Should().Be("malformed.json");
    }

    [Theory]
    [InlineData("{\"kind\":\"Command\",\"id\":\"test\"}")]
    [InlineData("{\"kind\":\"Toggle\",\"id\":\"test\"}")]
    [InlineData("{\"kind\":\"Flyout\",\"id\":\"test\"}")]
    [InlineData("{\"kind\":\"Provider\",\"id\":\"test\",\"provider\":\"missing\"}")]
    [InlineData("{\"kind\":\"Provider\",\"id\":\"test\",\"provider\":\"selection-readout\",\"children\":[]}")]
    [InlineData("{\"kind\":\"Separator\",\"id\":\"test\",\"command\":\"application.CopyCommand\"}")]
    [InlineData("{\"kind\":\"Separator\",\"id\":\"test\",\"checkedBinding\":\"missing\"}")]
    [InlineData("{\"kind\":\"Separator\",\"id\":\"test\",\"arguments\":{}}")]
    [InlineData("{\"kind\":\"Separator\",\"id\":\"test\",\"children\":[]}")]
    public void Invalid_shapes_are_attributed(string item)
    {
        var parse = () => ToolbarDeclaration.Parse("[{\"id\":\"band\",\"automationId\":\"Band\",\"visibilityBinding\":\"Settings.IsStandardToolbarVisible\",\"items\":[" + item + "]}]", "shape.json");
        var error = parse.Should().Throw<MenuDeclarationException>().Which;
        error.SourceName.Should().Be("shape.json");
        error.PlacementId.Should().Be("test");
    }

    [Fact]
    public void Nested_and_band_ids_share_one_namespace()
    {
        var bands = ToolbarDeclaration.Load();
        var flyout = bands[0].Items.Single(i => i.Children is not null);
        flyout.Children![0] = flyout.Children[0] with { Id = bands[1].Id };
        var validate = () => ToolbarDeclaration.Validate(bands, "duplicate.json");
        validate.Should().Throw<MenuDeclarationException>().Which.PlacementId.Should().Be(bands[1].Id);
    }

    [Theory]
    [InlineData((int)MenuArgumentKind.String, "\"text\"")]
    [InlineData((int)MenuArgumentKind.Boolean, "true")]
    [InlineData((int)MenuArgumentKind.Integer, "42")]
    public void Command_arguments_use_the_menu_signature_and_value_validation(int valueKind, string value)
    {
        var kind = (MenuArgumentKind)valueKind;
        var bands = ToolbarDeclaration.Load();
        var item = bands[0].Items.First(i => i.Kind == ToolbarItemKind.Command);
        var index = Array.IndexOf(bands[0].Items, item);
        var commands = new Dictionary<string, MenuCommandRegistration>(MenuHostRegistry.Commands)
        {
            ["typed"] = new("typed", null, null) { Arguments = new Dictionary<string, MenuArgumentKind> { ["value"] = kind } }
        };
        using var document = JsonDocument.Parse(value);
        bands[0].Items[index] = item with
        {
            Command = "typed", Arguments = new() { ["value"] = new(kind, document.RootElement.Clone()) }
        };
        var validate = () => ToolbarDeclaration.Validate(bands, "typed.json", commands);
        validate.Should().NotThrow();
        bands[0].Items[index] = bands[0].Items[index] with
        {
            Arguments = new() { ["value"] = new(kind, JsonSerializer.SerializeToElement(new[] { 1 })) }
        };
        validate.Should().Throw<MenuDeclarationException>().Which.PlacementId.Should().Be(item.Id);
        bands[0].Items[index] = item with { Command = "typed" };
        validate.Should().Throw<MenuDeclarationException>();
    }
}
