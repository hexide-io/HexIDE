using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Controls;

public class DragPreviewTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> ShippedStatuses => LanguageManifest.Packs.Where(pack => pack.Id != "en").SelectMany(pack =>
        new[] { "Dock", "Float", "None" }.Select(status => new object[] { pack.Id, status }));

    [AvaloniaTheory]
    [InlineData("en", "Dock")]
    [InlineData("en", "Float")]
    [InlineData("en", "None")]
    [MemberData(nameof(ShippedStatuses))]
    public void Status_fits_after_helper_measures_empty_preview(string language, string status)
    {
        var localization = new LocalizationService();
        // Dock's helper is internal; invoke the actual Show/Move path rather than reproducing its sizing.
        var helperType = typeof(DragPreviewControl).Assembly.GetType("Dock.Avalonia.Internal.DragPreviewHelper", true)!;
        var helper = Activator.CreateInstance(helperType, nonPublic: true)!;
        try
        {
            localization.Apply(language);
            var expected = localization.GetString("Str.Dock.DragPreviewControl" + status);
            helperType.GetMethod("Show")!.Invoke(helper, [new Tool { Title = "" }, new PixelPoint(100, 100), new PixelPoint(10, 10), null, null]);
            helperType.GetMethod("Move")!.Invoke(helper, [new PixelPoint(100, 100), new PixelPoint(10, 10), status]);
            Dispatcher.UIThread.RunJobs();
            var window = (Window)helperType.GetField("s_window", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            window.UpdateLayout();
            var text = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_StatusText");
            text.Text.Should().Be(expected);
            // Measure without the window constraint: constrained DesiredSize can itself hide clipping.
            var natural = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
                FontStyle = text.FontStyle, FontWeight = text.FontWeight };
            natural.Measure(Size.Infinity);
            output.WriteLine($"{language} {status}: {natural.DesiredSize.Width} DIP (font {text.FontSize})");
            text.DesiredSize.Width.Should().BeGreaterThanOrEqualTo(natural.DesiredSize.Width);
            // The horizontal StackPanel arranges text beyond its window: compare the visible arranged width.
            var origin = text.TranslatePoint(default, window)!.Value;
            var visibleBounds = new Rect(origin, text.Bounds.Size).Intersect(new Rect(window.ClientSize));
            visibleBounds.Width.Should().BeGreaterThanOrEqualTo(natural.DesiredSize.Width,
                "the status arrives after the helper locks the initial empty preview size");
        }
        finally
        {
            helperType.GetMethod("Hide")!.Invoke(helper, null);
            localization.Apply("en");
        }
    }
}
