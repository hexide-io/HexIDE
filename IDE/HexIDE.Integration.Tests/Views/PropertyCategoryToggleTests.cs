using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The box beside a Properties category shows '-' while the category is open and '+' while it is closed, as VB6's
/// categorized view does (#2228). Its theme key named a resource nothing defined, so it drew as a plain button: a grey
/// square with no sign, filled while open.
/// </summary>
public class PropertyCategoryToggleTests
{
    [AvaloniaFact]
    public void The_category_box_draws_a_minus_while_open_and_a_plus_while_closed()
    {
        var vm = new PropertiesToolViewModel(Substitute.For<HexIDE.IDE.IDocumentDockService>(),
            Substitute.For<HexIDE.IDE.IEventBus>(), Substitute.For<HexIDE.IDE.IWindowManager>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
        var category = new PropertyCategoryViewModel(vm, PropertyCategory.Appearance);
        var property = new PropertyViewModel(vm, VBProperties.BackColorProperty, null);
        vm.Properties.Add(property);
        vm.CategorizedProperties.Add(category);
        vm.CategorizedProperties.Add(property);
        var view = new PropertiesToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 400, Height = 300, Background = Brushes.White };
        window.Resources[Classic.CommonControls.SystemColors.WindowBrushKey] = new SolidColorBrush(Colors.White);
        window.Resources[Classic.CommonControls.SystemColors.WindowTextBrushKey] = new SolidColorBrush(Colors.Black);
        window.Resources[Classic.CommonControls.SystemColors.ControlDarkBrushKey] = new SolidColorBrush(Color.Parse("#808080"));
        window.Show();
        view.FindControl<TabItem>("CategorizedPropertiesTab")!.IsSelected = true;
        Dispatcher.UIThread.RunJobs();
        try
        {
            var toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(t => ReferenceEquals(t.DataContext, category));
            var plusStroke = toggle.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_PlusStroke");

            category.IsExpanded.Should().BeTrue();
            plusStroke.IsVisible.Should().BeFalse();
            var open = Pixels(window, toggle);

            category.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();
            plusStroke.IsVisible.Should().BeTrue();
            var closed = Pixels(window, toggle);

            // A sign is drawn, and the plus has the minus's stroke and one more.
            open.Dark.Should().BeGreaterThan(0);
            closed.Dark.Should().BeGreaterThan(open.Dark);
            // The box is the window's colour inside its border, not the filled square it was.
            open.Light.Should().BeGreaterThan(open.Dark * 2);
            closed.Light.Should().BeGreaterThan(closed.Dark * 2);
        }
        finally { window.Close(); }
    }

    // Counts near-black and near-white pixels inside the toggle, from a real Skia frame.
    private static (int Dark, int Light) Pixels(Window window, Control toggle)
    {
        using var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        var scale = window.RenderScaling;
        var origin = toggle.TranslatePoint(new Point(0, 0), window)!.Value;
        using var buffer = frame!.Lock();
        int dark = 0, light = 0;
        for (var y = (int)(origin.Y * scale); y < (int)((origin.Y + toggle.Bounds.Height) * scale); y++)
        for (var x = (int)(origin.X * scale); x < (int)((origin.X + toggle.Bounds.Width) * scale); x++)
        {
            var bgra = Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
            var luminance = (((bgra >> 16) & 0xFF) + ((bgra >> 8) & 0xFF) + (bgra & 0xFF)) / 3;
            if (luminance < 64) dark++;
            else if (luminance > 224) light++;
        }
        return (dark, light);
    }
}
