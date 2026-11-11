using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using HexIDE.Themes;
using HexIDE.Tools;
using HexIDE.VisualDesigner.Views;
using Avalonia.Styling;
using Avalonia.Threading;

namespace HexIDE.Integration.Tests.Controls;

public sealed class ChromeBorderTests
{
    [AvaloniaTheory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void FocusMovesBetweenControlsAndThemeSwitchRestoresTheirBorders(string pack)
    {
        var properties = new PropertiesToolView();
        var categoryToggle = new ToggleButton { Theme = (ControlTheme)properties.Resources["PropertyCategoryToggle"]! };
        TemplatedControl[] controls = [new Button(), new ToggleButton(), new CheckBox(), new RadioButton(), new TextBox(), new ComboBox(), new ListBoxItem(), new TabItem(), categoryToggle];
        var panel = new StackPanel();
        foreach (var control in controls) panel.Children.Add(control);
        var window = new Window { Content = panel };
        var service = new ThemeService();
        try
        {
            service.Apply("Classic");
            window.Show();
            var original = controls.Select(c => c.BorderThickness).ToArray();
            service.Apply(pack);
            window.UpdateLayout();
            foreach (var control in controls)
            {
                control.Focus(Avalonia.Input.NavigationMethod.Tab).Should().BeTrue();
                if (control is Button or ToggleButton or CheckBox or RadioButton or TextBox or ComboBox)
                {
                    control.FocusAdorner.Should().NotBeNull(control.GetType().Name);
                    continue;
                }
                control.BorderThickness.Should().Be(new Thickness(2), control.GetType().Name);
                var part = control.GetVisualDescendants().FirstOrDefault(c => c is Border or ContentPresenter);
                part.Should().NotBeNull(control.GetType().Name);
                if (part is Border border) CheckBorder(border.BorderThickness, border.BorderBrush, window);
                if (part is ContentPresenter presenter) CheckBorder(presenter.BorderThickness, presenter.BorderBrush, window);
            }
            var child = new TreeViewItem { Header = "Child" };
            var parent = new TreeViewItem { Header = "Parent", IsExpanded = true, Items = { child } };
            panel.Children.Add(new TreeView { Items = { parent } });
            window.UpdateLayout();
            child.Focus().Should().BeTrue();
            var childBorder = child.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SelectionBorder");
            CheckBorder(childBorder.BorderThickness, childBorder.BorderBrush, window);
            var parentBorder = parent.GetVisualDescendants().OfType<Border>().First(b => b.Name == "SelectionBorder");
            if (parentBorder.BorderThickness != default)
                parentBorder.BorderBrush.Should().Be(Brushes.Transparent, "focus on a child must not outline its ancestors");
            service.Apply("Classic");
            window.UpdateLayout();
            controls.Select(c => c.BorderThickness).Should().Equal(original);
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void SelectedItemsHaveContrastingBordersInTheirDrawingParts(string pack)
    {
        TemplatedControl[] items = [new ListBoxItem { IsSelected = true }, new ComboBoxItem { IsSelected = true },
            new TreeViewItem { IsSelected = true, Header = "Tree" }, new TabItem { IsSelected = true },
            new MenuItem { IsSelected = true, Header = "Menu" }, new DocumentTabStripItem { IsSelected = true },
            new ToolTabStripItem { IsSelected = true }];
        var panel = new StackPanel();
        foreach (var item in items) panel.Children.Add(item);
        var palette = new ColorPaletteToolView
        {
            DataContext = new ColorPaletteToolViewModel(Substitute.For<HexIDE.IDE.IDocumentDockService>(),
                Substitute.For<HexIDE.Localization.ILocalizationService>())
        };
        panel.Children.Add(palette);
        var window = new Window { Content = panel };
        var service = new ThemeService();
        try
        {
            service.Apply(pack);
            window.Show();
            var list = palette.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedIndex = 0;
            window.UpdateLayout();
            var selected = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.IsSelected);
            var frame = selected.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            CheckBorder(frame.BorderThickness, frame.BorderBrush, window);
            SyntaxHighlightingTheme.ContrastRatio(((ISolidColorBrush)frame.BorderBrush!).Color,
                ((ISolidColorBrush)frame.Background!).Color).Should().BeGreaterThanOrEqualTo(3);
            foreach (var item in items)
            {
                CheckBorder(item.BorderThickness, item.BorderBrush, window);
                item.GetVisualDescendants().Should().Contain(c =>
                    c is Border && ((Border)c).BorderThickness == new Thickness(2) ||
                    c is ContentPresenter && ((ContentPresenter)c).BorderThickness == new Thickness(2), item.GetType().Name);
            }
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }

    [AvaloniaFact]
    public void SwitchingPacksAddsAndRemovesBorderStylesExactlyOnce()
    {
        var app = Application.Current!;
        var service = new ThemeService();
        int BorderStyles() => app.Styles.OfType<StyleInclude>()
            .Count(style => style.Source?.AbsolutePath == "/Themes/ChromeBorders.axaml");
        try
        {
            foreach (var pack in new[] { "Classic", "HighContrastLight", "HighContrastLight", "HighContrastDark",
                         "Dark", "HighContrastDark", "Abyss", "HighContrastLight", "Classic", "Classic" })
            {
                service.Apply(pack);
                BorderStyles().Should().Be(pack.StartsWith("HighContrast", System.StringComparison.Ordinal) ? 1 : 0, pack);
            }
        }
        finally
        {
            service.Apply("Classic");
        }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void ClassicRoundTripRestoresExistingMenuAndToolChrome(string pack)
    {
        var menuItem = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Open" } } };
        var layout = Automation.DragDockTests.Shown();
        var window = layout.Window;
        window.Content = null;
        var view = new MainView();
        var placeholder = view.FindControl<DockControl>("DockControl")!;
        var parent = (Panel)placeholder.Parent!;
        var index = parent.Children.IndexOf(placeholder);
        parent.Children.RemoveAt(index);
        foreach (var style in placeholder.Styles.ToArray())
        {
            placeholder.Styles.Remove(style);
            layout.Host.Styles.Add(style);
        }
        parent.Children.Insert(index, layout.Host);
        view.FindControl<Classic.Avalonia.Theme.ClassicBorderDecorator>("MainMenuHost")!.Child =
            new Menu { Items = { menuItem, new MenuItem { Header = "Edit" } } };
        window.Content = view;
        var service = new ThemeService(string.Empty, () => [window]);
        try
        {
            service.Apply("Classic");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var close = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_CloseButton");
            close.Bounds.Width.Should().BeGreaterThan(0);
            var controls = view.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible).ToArray();
            Dispatcher.UIThread.RunJobs();
            using var initialFrame = window.CaptureRenderedFrame();
            var original = controls.Select(Geometry).ToArray();
            menuItem.IsSubMenuOpen = true;
            window.UpdateLayout();
            var originalOpen = controls.Select(Geometry).ToArray();
            menuItem.IsSubMenuOpen = false;
            void CheckGeometry(bool open)
            {
                var expected = open ? originalOpen : original;
                for (var i = 0; i < controls.Length; ++i)
                    Geometry(controls[i]).Should().Be(expected[i], controls[i].GetType().Name + " " + controls[i].Name + " open=" + open);
            }
            using var scope = new AwesomeAssertions.Execution.AssertionScope();
            foreach (var id in new[] { pack, "Classic", pack, "Classic" })
            {
                service.Apply(id);
                menuItem.IsSelected = id != "Classic";
                menuItem.IsSubMenuOpen = id != "Classic";
                close.Focus();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                if (id == "Classic")
                {
                    CheckGeometry(false);
                    menuItem.IsSubMenuOpen = true;
                    window.UpdateLayout();
                    CheckGeometry(true);
                    menuItem.IsSubMenuOpen = false;
                }
            }
        }
        finally { window.Close(); service.Apply("Classic"); }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void FocusAndSelectionDoNotMoveLayout(string pack)
    {
        var combo = new ComboBox { Items = { "One", "Two" } };
        var item = new ListBoxItem { Content = "One" };
        var treeItem = new TreeViewItem { Header = "Tree" };
        var other = new Button { Content = "Focus elsewhere" };
        var panel = new StackPanel { Children = { combo, new ListBox { Items = { item, "Two" } },
            new TreeView { Items = { treeItem } }, other } };
        var window = new Window { Width = 600, Height = 400, Content = panel };
        var service = new ThemeService();
        try
        {
            service.Apply(pack);
            window.Show();
            other.Focus();
            window.UpdateLayout();
            var original = panel.GetVisualDescendants().OfType<Control>().Select(c => (c, c.Bounds)).ToArray();
            combo.Focus().Should().BeTrue();
            item.IsSelected = true;
            treeItem.IsSelected = true;
            window.UpdateLayout();
            using var scope = new AwesomeAssertions.Execution.AssertionScope();
            foreach (var (control, bounds) in original)
                control.Bounds.Should().Be(bounds, control.GetType().Name + " " + control.Name);
        }
        finally { window.Close(); service.Apply("Classic"); }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight")]
    [InlineData("HighContrastDark")]
    public void UnfocusedInputsKeepTheirOwnOutlines(string pack)
    {
        TemplatedControl[] controls = [new TextBox(), new ComboBox(), new CheckBox(), new ListBox(), new Button()];
        var panel = new StackPanel();
        foreach (var control in controls) panel.Children.Add(control);
        var window = new Window { Content = panel };
        var service = new ThemeService();
        try
        {
            service.Apply(pack);
            window.Show();
            window.Focus();
            window.UpdateLayout();
            using var scope = new AwesomeAssertions.Execution.AssertionScope();
            foreach (var control in controls)
            {
                var part = control.GetVisualDescendants().First(c => c is Border or ContentPresenter);
                var (thickness, brush) = part is Border border
                    ? (border.BorderThickness, border.BorderBrush)
                    : (((ContentPresenter)part).BorderThickness, ((ContentPresenter)part).BorderBrush);
                thickness.Should().NotBe(default(Thickness), control.GetType().Name);
                brush.Should().BeAssignableTo<ISolidColorBrush>().Which.Color.A.Should().BeGreaterThan(0, control.GetType().Name);
            }
        }
        finally { window.Close(); service.Apply("Classic"); }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight", false, 1.0)]
    [InlineData("HighContrastLight", true, 1.0)]
    [InlineData("HighContrastDark", false, 1.0)]
    [InlineData("HighContrastDark", true, 1.0)]
    [InlineData("HighContrastLight", false, 1.5)]
    [InlineData("HighContrastLight", true, 1.5)]
    [InlineData("HighContrastDark", false, 1.5)]
    [InlineData("HighContrastDark", true, 1.5)]
    public void KeyboardFocusRendersOutsideInputWithoutChangingGeometry(string pack, bool button, double scaling)
    {
        TemplatedControl control = button ? new Button { Content = "Action" } : new TextBox();
        control.Width = 120;
        control.Height = 32;
        var other = new Button { Content = "Other", Width = 120, Height = 32 };
        var panel = new Canvas { Children = { control, other } };
        Canvas.SetLeft(other, 20);
        Canvas.SetTop(other, 80);
        Canvas.SetLeft(control, 20);
        Canvas.SetTop(control, 20);
        panel.Classes.Add("ideDialog");
        var window = new Window { Content = panel, Width = 180, Height = 140 };
        var service = new ThemeService();
        try
        {
            service.Apply(pack);
            window.Show();
            window.SetRenderScaling(scaling);
            other.Focus(Avalonia.Input.NavigationMethod.Tab).Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            var original = Geometry(control);
            using var before = window.CaptureRenderedFrame()!;
            var unfocused = OutsideBand(before, window, control);
            var color = ((ISolidColorBrush)window.FindResource("ThemeForegroundBrush")!).Color;
            var foreground = (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
            unfocused.Should().NotContain(foreground);

            control.Focus(Avalonia.Input.NavigationMethod.Tab).Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            using var focused = window.CaptureRenderedFrame()!;
            Geometry(control).Should().Be(original);
            OutsideBand(focused, window, control).Should().OnlyContain(pixel => pixel == foreground,
                "the 2 DIP band on all four sides must actually draw the pack's foreground, outside the control's clip");

            other.Focus(Avalonia.Input.NavigationMethod.Tab).Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            using var after = window.CaptureRenderedFrame()!;
            OutsideBand(after, window, control).Should().Equal(unfocused,
                "removing keyboard focus must restore every pixel outside the control");
            Geometry(control).Should().Be(original);
        }
        finally { window.Close(); service.Apply("Classic"); }
    }

    private static uint[] OutsideBand(Avalonia.Media.Imaging.WriteableBitmap frame, Window window, Control control)
    {
        var size = frame.PixelSize;
        var pixels = new uint[size.Width * size.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { frame.CopyPixels(new PixelRect(default, size), handle.AddrOfPinnedObject(), pixels.Length * 4, size.Width * 4); }
        finally { handle.Free(); }
        var scale = size.Width / window.Bounds.Width;
        var origin = control.TranslatePoint(default, window)!.Value;
        var left = (int)System.Math.Round(origin.X * scale);
        var top = (int)System.Math.Round(origin.Y * scale);
        var right = (int)System.Math.Round((origin.X + control.Bounds.Width) * scale);
        var bottom = (int)System.Math.Round((origin.Y + control.Bounds.Height) * scale);
        var band = (int)System.Math.Round(2 * scale);
        var result = new System.Collections.Generic.List<uint>();
        for (var y = top - band; y < bottom + band; y++)
            for (var x = left - band; x < right + band; x++)
                if ((x >= left && x < right && (y < top || y >= bottom)) ||
                    (y >= top && y < bottom && (x < left || x >= right)))
                    result.Add(pixels[y * size.Width + x]);
        result.Should().NotBeEmpty();
        return result.ToArray();
    }

    private static (Rect Bounds, Thickness Border, Thickness Padding) Geometry(Control control) => control switch
    {
        TemplatedControl templated => (control.Bounds, templated.BorderThickness, templated.Padding),
        Border border => (control.Bounds, border.BorderThickness, border.Padding),
        ContentPresenter presenter => (control.Bounds, presenter.BorderThickness, presenter.Padding),
        _ => (control.Bounds, default, default)
    };

    private static void CheckBorder(Thickness thickness, IBrush? brush, Window window)
    {
        thickness.Should().Be(new Thickness(2));
        var background = (ISolidColorBrush)window.FindResource("ThemeBackgroundBrush")!;
        SyntaxHighlightingTheme.ContrastRatio(((ISolidColorBrush)brush!).Color, background.Color)
            .Should().BeGreaterThanOrEqualTo(3);
    }
}
