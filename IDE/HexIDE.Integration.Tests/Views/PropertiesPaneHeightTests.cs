using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

// Pane sizes and shrink priorities: https://github.com/hexide-io/HexIDE/issues/4090.
public class PropertiesPaneHeightTests
{
    [AvaloniaTheory]
    [InlineData(185, 0)]
    [InlineData(185, 1)]
    [InlineData(120, 0)]
    [InlineData(120, 1)]
    [InlineData(285, 0)]
    [InlineData(285, 1)]
    public void Short_panes_keep_the_tabs_and_rows_before_the_description(double height, int selectedTab)
    {
        var localization = Substitute.For<ILocalizationService>();
        var vm = new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), localization, new Vb6ProjectFileFormat());
        for (var i = 0; i < 20; i++)
        {
            var property = new PropertyViewModel(vm, VBProperties.CaptionProperty, null);
            vm.Properties.Add(property);
            vm.CategorizedProperties.Add(property);
        }
        vm.SelectedProperty = vm.Properties[0];
        var view = new PropertiesToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 260, Height = height };
        window.SetRenderScaling(1.5);
        try
        {
            window.Show();
            var tabs = view.FindControl<TabControl>("TabProperties")!;
            tabs.SelectedIndex = selectedTab;
            window.UpdateLayout();
            view.Bounds.Height.Should().BeApproximately(height, 0.5);
            foreach (var tab in new[] { "AlphabeticPropertiesTab", "CategorizedPropertiesTab" })
            {
                var header = view.FindControl<TabItem>(tab)!;
                var bounds = Bounds(header, view);
                bounds.Height.Should().BeGreaterThan(16);
                bounds.Top.Should().BeGreaterThanOrEqualTo(0);
                bounds.Bottom.Should().BeLessThanOrEqualTo(view.Bounds.Height + 0.000001);
                bounds.Height.Should().BeGreaterThanOrEqualTo(header.DesiredSize.Height);
            }
            var list = view.FindControl<ListBox>(selectedTab == 0 ? "AlphabeticProperties" : "CategorizedProperties")!;
            if (height >= 185)
            {
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
                scroll.Viewport.Height.Should().BeGreaterThanOrEqualTo(32);
                scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height);
                scroll.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical)
                    .IsEffectivelyVisible.Should().BeTrue();
                var first = list.ContainerFromIndex(0)!;
                first.Should().NotBeNull();
                first.IsEffectivelyVisible.Should().BeTrue();
                var row = Bounds(first, view);
                row.Height.Should().BeGreaterThanOrEqualTo(16);
                row.Top.Should().BeGreaterThanOrEqualTo(Bounds(list, view).Top);
                row.Bottom.Should().BeLessThanOrEqualTo(view.Bounds.Height + 0.000001);
                row.Bottom.Should().BeLessThanOrEqualTo(Bounds(list, view).Bottom);
            }
            var description = view.GetVisualDescendants().OfType<Classic.Avalonia.Theme.ClassicBorderDecorator>().Single();
            if (height == 285)
            {
                description.Bounds.Height.Should().Be(80);
                Bounds(description, view).Bottom.Should().BeLessThanOrEqualTo(view.Bounds.Height + 0.000001);
            }
            else
                Bounds(description, view).Intersect(new Rect(view.Bounds.Size)).Height.Should().BeLessThan(80);
        }
        finally { window.Close(); }
    }

    private static Rect Bounds(Control control, Control relativeTo) =>
        new(control.TranslatePoint(default, relativeTo)!.Value, control.Bounds.Size);
}
