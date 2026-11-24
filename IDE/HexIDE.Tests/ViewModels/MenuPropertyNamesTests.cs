using System.Globalization;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Tests.ViewModels;

public class MenuPropertyNamesTests
{
    [Fact]
    public void Registered_classes_lookup_exactly_their_declared_properties()
    {
        var registry = new ComponentRegistry();
        foreach (var component in registry.Components.Concat(registry.LibraryControls.Select(c => c.Class))
                     .Concat([LineComponentClass.Instance, MenuComponentClass.Instance]))
            component.PropertiesByName.Keys.Should().BeEquivalentTo(component.Properties.Select(p => p.Name), component.VBTypeName);
    }

    [Theory]
    [InlineData(MenuPropertyNames.SubItems, typeof(List<ComponentInstance>))]
    [InlineData(MenuPropertyNames.Shortcut, typeof(string))]
    [InlineData(MenuPropertyNames.NegotiatePosition, typeof(int))]
    public void Each_name_resolves_on_the_provider_menu(string name, Type type)
    {
        var menuClass = MenuComponentClass.Instance;
        PropertyClass property = name switch
        {
            MenuPropertyNames.SubItems => MenuPropertyNames.SubItemsProperty,
            MenuPropertyNames.Shortcut => MenuPropertyNames.ShortcutProperty(menuClass),
            MenuPropertyNames.NegotiatePosition => MenuPropertyNames.NegotiatePositionProperty(menuClass),
            _ => throw new InvalidOperationException(name)
        };
        property.PropertyType.Should().Be(type);
        property.Should().BeSameAs(name == MenuPropertyNames.SubItems
            ? MenuComponentClass.SubItemsProperty : menuClass.PropertiesByName[name]);
    }

    [AvaloniaFact]
    public void Converter_resolves_the_property_name_on_the_instances_class()
    {
        var children = new List<ComponentInstance>();
        var menu = new ComponentInstance(MenuComponentClass.Instance, "Menu1")
            .SetProperty(MenuComponentClass.SubItemsProperty, children);
        var form = new FormDefinition(TestHelpers.CreateProject(), FormComponentClass.Instance, "Form1");
        using var designer = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var instance = new ComponentInstanceViewModel(designer, menu);
        PropertyConverter.Instance.Convert(instance, typeof(object), MenuPropertyNames.SubItems, CultureInfo.InvariantCulture)
            .Should().BeSameAs(children);
    }
}
