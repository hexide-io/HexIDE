using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class PropertiesLocalizationTests
{
    [Fact]
    public void Language_change_refreshes_selected_description_without_replacing_rows()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.GetPropertyDescription("Caption").Returns("English description");
        var grid = new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), loc);
        var row = new PropertyViewModel(grid, VBProperties.CaptionProperty, "Form1");
        grid.Properties.Add(row);
        grid.CategorizedProperties.Add(row);
        grid.SelectedProperty = row;
        var changes = new List<string?>();
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        loc.GetPropertyDescription("Caption").Returns("Description française");
        loc.LanguageChanged += Raise.Event<Action>();

        grid.SelectedProperty.Should().BeSameAs(row);
        row.Description.Should().Be("Description française");
        changes.Should().Contain(nameof(row.Description));
        row.Value.Should().Be("Form1");
    }

    [Fact]
    public void Language_change_refreshes_category_header_and_accessible_name_preserving_expansion()
    {
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.PropCategory.Appearance").Returns("Appearance");
        var grid = new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), loc);
        var category = new PropertyCategoryViewModel(grid, PropertyCategory.Appearance) { IsExpanded = false };
        grid.CategorizedProperties.Add(category);
        var changes = new List<string?>();
        category.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        loc.GetString("Str.PropCategory.Appearance").Returns("Apparence");
        loc.LanguageChanged += Raise.Event<Action>();

        category.Header.Should().Be("Apparence");
        category.AccessibleName.Should().Be("Apparence");
        changes.Should().Contain(nameof(category.Header)).And.Contain(nameof(category.AccessibleName));
        category.IsExpanded.Should().BeFalse();
        grid.CategorizedProperties[0].Should().BeSameAs(category);
    }
}
