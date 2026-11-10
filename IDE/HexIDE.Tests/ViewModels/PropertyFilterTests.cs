using Avalonia.Headless.XUnit;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class PropertyFilterTests
{
    [AvaloniaTheory]
    [InlineData("cAp")]
    [InlineData("no-such-property")]
    public void Filter_narrows_both_lists_removes_empty_categories_and_clears(string text)
    {
        var bus = new EventBus();
        var dock = Substitute.For<IDocumentDockService>();
        var loc = Substitute.For<ILocalizationService>();
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        using var designer = new FormEditViewModel(null!, bus, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(), loc, new Vb6ProjectFileFormat())
            .Initialize(project.Forms.First());
        var grid = new PropertiesToolViewModel(dock, bus, Substitute.For<IWindowManager>(), loc, new Vb6ProjectFileFormat());
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        var original = grid.Properties.OfType<PropertyViewModel>().ToArray();
        original.Length.Should().BeGreaterThan(1);
        var categorized = grid.CategorizedProperties.ToArray();
        var collapsed = grid.CategorizedProperties.OfType<PropertyCategoryViewModel>().First();
        collapsed.IsExpanded = false;
        var filter = grid.GetType().GetProperty("FilterText");
        filter.Should().NotBeNull();
        filter!.SetValue(grid, text);
        var expected = original.Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        grid.Properties.Should().Equal(expected);
        grid.CategorizedProperties.OfType<PropertyViewModel>().Should().BeEquivalentTo(expected);
        grid.CategorizedProperties.OfType<PropertyCategoryViewModel>().Select(c => c.Category)
            .Should().BeEquivalentTo(expected.Select(p => p.PropertyClass.Category).Distinct());
        filter.SetValue(grid, "");
        grid.Properties.Should().Equal(original);
        grid.CategorizedProperties.Should().Equal(categorized);
        collapsed.IsExpanded.Should().BeFalse();
        grid.Properties.OfType<PropertyViewModel>().Where(p => p.PropertyClass.Category == collapsed.Category)
            .Should().OnlyContain(p => !p.IsVisible);
    }
}
