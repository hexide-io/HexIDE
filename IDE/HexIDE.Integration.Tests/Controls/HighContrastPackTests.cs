using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views.Options;
using HexIDE.IDE;
using HexIDE.Themes;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Controls;

public sealed class HighContrastPackTests
{
    [AvaloniaTheory]
    [InlineData("HighContrastLight", "Properties")]
    [InlineData("HighContrastDark", "Properties")]
    [InlineData("HighContrastLight", "Document")]
    [InlineData("HighContrastDark", "Document")]
    public void SelectedTabsContrastWithUnselectedTabs(string id, string part)
    {
        var service = new ThemeService();
        var view = new PropertiesToolView();
        var tabs = view.FindControl<TabControl>("TabProperties")!;
        ((TabItem)tabs.Items[0]!).Header = "Alphabetic";
        ((TabItem)tabs.Items[1]!).Header = "Categorized";
        tabs.SelectedIndex = 0;
        var document = new DocumentTabStripItem { IsSelected = true, IsActive = true };
        var window = new Window { Content = new StackPanel { Children = { view, document } } };
        try
        {
            service.Apply(id);
            window.Show();
            Color Resource(string key) => ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, key)!).Color;
            Color selected;
            Color unselected;
            Color foreground;
            if (part == "Properties")
            {
                var presenters = tabs.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                    .Where(t => t.Name == "PART_ContentPresenter" && t.Content is string).ToArray();
                var active = presenters.Single(t => Equals(t.Content, "Alphabetic"));
                var inactive = presenters.Single(t => Equals(t.Content, "Categorized"));
                active.BorderThickness.Left.Should().BeGreaterThanOrEqualTo(2);
                inactive.BorderThickness.Should().Be(new Thickness(2));
                ((ISolidColorBrush)inactive.BorderBrush!).Color.A.Should().Be(0);
                selected = ((ISolidColorBrush)active.BorderBrush!).Color;
                unselected = Resource("ThemeBackgroundBrush");
                foreground = ((ISolidColorBrush)active.Foreground!).Color;
                SyntaxHighlightingTheme.ContrastRatio(foreground, ((ISolidColorBrush)active.Background!).Color)
                    .Should().BeGreaterThanOrEqualTo(7);
            }
            else
            {
                document.BorderThickness.Left.Should().BeGreaterThanOrEqualTo(2);
                selected = ((ISolidColorBrush)document.BorderBrush!).Color;
                unselected = Resource("DockDocumentTabStripBackgroundBrush");
                foreground = ((ISolidColorBrush)document.Foreground!).Color;
                SyntaxHighlightingTheme.ContrastRatio(foreground, ((ISolidColorBrush)document.Background!).Color)
                    .Should().BeGreaterThanOrEqualTo(7);
                document.IsActive = false;
                document.BorderThickness.Left.Should().BeGreaterThanOrEqualTo(2);
                document.IsSelected = false;
                document.BorderThickness.Should().Be(new Thickness(2));
                ((ISolidColorBrush)document.BorderBrush!).Color.A.Should().Be(0);
            }
            SyntaxHighlightingTheme.ContrastRatio(selected, unselected).Should().BeGreaterThanOrEqualTo(3);
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight", "LineNumbers")]
    [InlineData("HighContrastLight", "Placeholder")]
    [InlineData("HighContrastLight", "Tab")]
    [InlineData("HighContrastLight", "ScrollBar")]
    [InlineData("HighContrastLight", "Dock")]
    [InlineData("HighContrastDark", "LineNumbers")]
    [InlineData("HighContrastDark", "Placeholder")]
    [InlineData("HighContrastDark", "Tab")]
    [InlineData("HighContrastDark", "ScrollBar")]
    [InlineData("HighContrastDark", "Dock")]
    public void DrawnChromeMeetsContrastThresholds(string id, string part)
    {
        var service = new ThemeService();
        var editor = new AvaloniaEdit.TextEditor { ShowLineNumbers = true, Text = "Sub Main()" };
        var input = new TextBox { PlaceholderText = "Filter" };
        var tabs = new TabControl { ItemsSource = new[] { new TabItem { Header = "Properties" } } };
        var scroll = new Avalonia.Controls.Primitives.ScrollBar { Maximum = 100, Value = 30, ViewportSize = 10 };
        var window = new Window { Content = new StackPanel { Children = { editor, input, tabs, scroll } } };
        try
        {
            service.Apply(id);
            window.Show();
            Color Resource(string key) => ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, key)!).Color;
            void Contrast(IBrush? foreground, Color background, double minimum) =>
                SyntaxHighlightingTheme.ContrastRatio(((ISolidColorBrush)foreground!).Color, background)
                    .Should().BeGreaterThanOrEqualTo(minimum);
            var background = Resource("ThemeBackgroundBrush");
            if (part == "LineNumbers") Contrast(editor.LineNumbersForeground, background, 7);
            if (part == "Placeholder")
            {
                var placeholder = input.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Name == "placeholder");
                var ink = ((ISolidColorBrush)placeholder.Foreground!).Color;
                var alpha = ink.A / 255d * placeholder.Opacity;
                var composite = Color.FromRgb((byte)(ink.R * alpha + background.R * (1 - alpha)),
                    (byte)(ink.G * alpha + background.G * (1 - alpha)),
                    (byte)(ink.B * alpha + background.B * (1 - alpha)));
                SyntaxHighlightingTheme.ContrastRatio(composite, background).Should().BeGreaterThanOrEqualTo(7);
            }
            if (part == "Tab")
            {
                var tab = tabs.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                    .First(t => t.Name == "PART_ContentPresenter" && t.Content is string);
                Contrast(tab.Foreground, ((ISolidColorBrush)tab.Background!).Color, 7);
            }
            if (part == "ScrollBar")
            {
                var thumb = scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();
                Contrast(thumb.Background, Resource("ThemeControlMidBrush"), 3);
            }
            if (part == "Dock")
            {
                Resource("DockThemeBackgroundBrush").Should().Be(background);
                Resource("DockDocumentTabStripBackgroundBrush").Should().Be(background);
            }
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight", false)]
    [InlineData("HighContrastDark", true)]
    public void SelectionAppliesThePackAndShowsALocalizedName(string id, bool dark)
    {
        var service = new ThemeService();
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveTheme.Returns(id);
        var vm = new ThemePageViewModel(settings, service);
        var view = new ThemePageView { DataContext = vm };
        view.Resources["Str.Theme." + id] = "Translated name";
        var window = new Window { Content = view, Width = 700, Height = 400 };
        try
        {
            service.Apply(id);
            service.ActiveTheme.Should().Be(id);
            Application.Current!.RequestedThemeVariant.Should().Be(dark ? ThemeVariant.Dark : ThemeVariant.Light);
            ((ISolidColorBrush)Application.Current.FindResource(Classic.CommonControls.SystemColors.WindowBrushKey)!)
                .Color.Should().Be(dark ? Colors.Black : Colors.White);
            window.Show();
            var box = view.GetVisualDescendants().OfType<ComboBox>().Single();
            box.SelectedItem.Should().Be(id);
            box.GetVisualDescendants().OfType<TextBlock>().Should()
                .Contain(text => text.IsEffectivelyVisible && text.Text == "Translated name");
            view.Resources["Str.Theme." + id] = "New translation";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            box.GetVisualDescendants().OfType<TextBlock>().Should()
                .Contain(text => text.IsEffectivelyVisible && text.Text == "New translation");
            // Pressing OK without choosing a theme leaves the setting alone, so the IDE keeps following the system (#3300).
            vm.SaveToSettings();
            settings.DidNotReceive().ActiveTheme = Arg.Any<string>();
            vm.SelectedTheme = "Classic";
            vm.SelectedTheme = id;
            vm.SaveToSettings();
            settings.Received().ActiveTheme = id;
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }
}
