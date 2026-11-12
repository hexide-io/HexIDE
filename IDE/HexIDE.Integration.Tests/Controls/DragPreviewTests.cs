using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
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
    public void Document_status_fits_alongside_title(string language, string status)
        => CheckStatus(language, status, document: true);

    [AvaloniaTheory]
    [InlineData("en", "Dock")]
    [InlineData("en", "Float")]
    [InlineData("en", "None")]
    [MemberData(nameof(ShippedStatuses))]
    public void Status_fits_after_helper_measures_empty_preview(string language, string status)
        => CheckStatus(language, status, document: false);

    private void CheckStatus(string language, string status, bool document)
    {
        var localization = new LocalizationService();
        Window? tabWindow = null;
        // Dock's helper is internal; invoke the actual Show/Move path rather than reproducing its sizing.
        var helperType = typeof(DragPreviewControl).Assembly.GetType("Dock.Avalonia.Internal.DragPreviewHelper", true)!;
        var helper = Activator.CreateInstance(helperType, nonPublic: true)!;
        try
        {
            localization.Apply(language);
            var expected = localization.GetString("Str.Dock.DragPreviewControl" + status);
            var title = localization.GetString("Str.Tool.ObjectBrowser.Chrome.Title");
            var dockable = document ? (IDockable)new Document { Title = title, CanFloat = false } : new Tool { Title = "" };
            Size? preferredSize = null;
            if (document)
            {
                var tabs = new DocumentControl
                {
                    DataContext = new DocumentDock
                    {
                        VisibleDockables = new ObservableCollection<IDockable> { dockable }, ActiveDockable = dockable,
                    },
                };
                tabWindow = new Window { Content = tabs };
                tabWindow.Show();
                tabWindow.UpdateLayout();
                var tab = tabs.GetVisualDescendants().OfType<DocumentTabStripItem>().Single();
                var stateType = helperType.Assembly.GetType("Dock.Avalonia.Internal.DockControlState", true)!;
                preferredSize = (Size?)stateType.GetMethod("GetPreferredPreviewSize", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [tab]);
                preferredSize.Should().NotBeNull();
            }
            helperType.GetMethod("Show")!.Invoke(helper, [dockable, new PixelPoint(100, 100), new PixelPoint(10, 10), null, preferredSize]);
            helperType.GetMethod("Move")!.Invoke(helper, [new PixelPoint(100, 100), new PixelPoint(10, 10), status]);
            Dispatcher.UIThread.RunJobs();
            var window = (Window)helperType.GetField("s_window", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            window.UpdateLayout();
            var text = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_StatusText");
            output.WriteLine($"tab={preferredSize} window={window.ClientSize} title={title}");
            text.Text.Should().Be(expected);
            if (document)
            {
                var caption = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == title);
                var captionOrigin = caption.TranslatePoint(default, window)!.Value;
                new Rect(captionOrigin, caption.Bounds.Size).Intersect(new Rect(window.ClientSize)).Width
                    .Should().BeGreaterThanOrEqualTo(caption.DesiredSize.Width);
            }
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
            tabWindow?.Close();
            localization.Apply("en");
        }
    }
}
