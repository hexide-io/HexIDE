using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

public class PropertyDescriptionLayoutTests
{
    [AvaloniaFact]
    public void The_default_pane_fits_the_property_name_and_three_description_lines()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetPropertyDescription("Caption").Returns("First description line.\nSecond description line.\nThird description line.");
        var vm = new PropertiesToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), localization, new Vb6ProjectFileFormat());
        var property = new PropertyViewModel(vm, VBProperties.CaptionProperty, null);
        vm.SelectedProperty = property;
        var view = new PropertiesToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 260, Height = 400 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var description = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == property.Description);
            var pane = description.GetVisualAncestors().OfType<Classic.Avalonia.Theme.ClassicBorderDecorator>().First();
            var origin = description.TranslatePoint(default, pane)!.Value;

            description.TextLayout.TextLines.Count.Should().Be(3);
            (origin.Y + description.TextLayout.Height).Should().BeLessThanOrEqualTo(pane.Bounds.Height - pane.Padding.Bottom);
        }
        finally { window.Close(); }
    }
}
