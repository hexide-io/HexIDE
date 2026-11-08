using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;

namespace HexIDE.Integration.Tests.Views;

public class NewProjectLayoutTests
{
    private static Window Open()
    {
        var app = new App();
        app.LoadMarkup();
        var resources = app.Resources;
        app.Resources = new ResourceDictionary();
        var view = new NewProjectView
        {
            DataContext = new NewProjectViewModel(Substitute.For<IWindowManager>(),
                Substitute.For<IRecentProjectsService>(), Substitute.For<ISettingsService>(),
                new ProfileService(), Substitute.For<ILocalizationService>()),
        };
        var window = new DialogWindow
        {
            Content = view, DataContext = view.DataContext, SizeToContent = SizeToContent.WidthAndHeight,
        };
        window.Resources.MergedDictionaries.Add(resources);
        using var pack = AssetLoader.Open(new Uri("avares://HexIDE/Localization/Packs/en.json"));
        var strings = System.Text.Json.JsonSerializer.Deserialize(pack,
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        foreach (var (key, value) in strings)
            window.Resources[key] = value;
        while (app.Styles.Count > 0)
        {
            var style = app.Styles[0];
            app.Styles.RemoveAt(0);
            window.Styles.Add(style);
        }
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        view.Bounds.Size.Should().Be(new Size(470, 400));
        return window;
    }

    [AvaloniaFact]
    public void Default_dialog_wraps_all_templates_with_only_vertical_overflow()
    {
        var window = Open();
        try
        {
            var list = window.GetVisualDescendants().OfType<ListBox>().First();
            list.ItemCount.Should().Be(8);
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Extent.Width.Should().BeLessThanOrEqualTo(scroll.Viewport.Width);
            list.GetVisualDescendants().OfType<ScrollBar>()
                .Where(bar => bar.Orientation == Avalonia.Layout.Orientation.Horizontal)
                .Should().OnlyContain(bar => !bar.IsVisible);
            var items = Enumerable.Range(0, list.ItemCount).Select(i => list.ContainerFromIndex(i)!).ToArray();
            items.Select(item => item.Bounds.Y).Distinct().Should().HaveCountGreaterThan(1);
            foreach (var item in items)
            {
                item.Bounds.Left.Should().BeGreaterThanOrEqualTo(0);
                item.Bounds.Right.Should().BeLessThanOrEqualTo(scroll.Viewport.Width);
                item.Bounds.Bottom.Should().BeLessThanOrEqualTo(scroll.Extent.Height);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Keyboard_can_reach_each_template_without_horizontal_scrolling()
    {
        var window = Open();
        try
        {
            var list = window.GetVisualDescendants().OfType<ListBox>().First();
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            list.ContainerFromIndex(0)!.Focus();
            for (var i = 0; i < list.ItemCount; i++)
            {
                if (i > 0)
                {
                    window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                    window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                }
                list.SelectedIndex.Should().Be(i);
                var item = list.ContainerFromIndex(i)!;
                var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().Single();
                var origin = item.TranslatePoint(default, presenter)!.Value;
                origin.X.Should().BeGreaterThanOrEqualTo(0);
                (origin.X + item.Bounds.Width).Should().BeLessThanOrEqualTo(presenter.Bounds.Width);
                origin.Y.Should().BeGreaterThanOrEqualTo(0);
                (origin.Y + item.Bounds.Height).Should().BeLessThanOrEqualTo(presenter.Bounds.Height);
                scroll.Offset.X.Should().Be(0);
            }
        }
        finally { window.Close(); }
    }
}
