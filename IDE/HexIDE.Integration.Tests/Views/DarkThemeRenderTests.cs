using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using HexIDE.Themes;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Tools.TranslationEditor;
using Dock.Avalonia.Controls;
using HexIDE.Forms.Views;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Tools;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

public class DarkThemeRenderTests : WindowTestFixture
{
    [AvaloniaTheory]
    [InlineData("Abyss", "#000C18")]
    [InlineData("Dark", "#252526")]
    public async Task MainWindowPanesUseThePacksBackground(string id, string expected)
    {
        var previous = Static.StartupProjectHook;
        Static.StartupProjectHook = (_, _) => Task.CompletedTask;
        var service = new ThemeService();
        await using var owned = new RealComposition();
        var dictionaries = Application.Current!.Resources.MergedDictionaries;
        var originalDictionaries = dictionaries.ToArray();
        owned.Composition.LocalizationService.Apply("en");
        var root = owned.Composition.Root;
        root.ResetWindowLayout();
        var window = new MainWindow { DataContext = root, Width = 1200, Height = 800 };
        var locator = new ViewLocator();
        Application.Current!.DataTemplates.Add(locator);
        try
        {
            service.Apply(id);
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            var evidence = Environment.GetEnvironmentVariable("HEXIDE_THEME_EVIDENCE");
            if (evidence is not null)
            {
                Directory.CreateDirectory(evidence);
                frame!.Save(Path.Combine(evidence, id + ".png"), PngBitmapEncoderOptions.Default);
            }
            var readings = new List<string>();
            using var assertions = new AwesomeAssertions.Execution.AssertionScope();
            foreach (var name in new[] { nameof(ProjectToolView), nameof(PropertiesToolView), nameof(ToolBoxToolView) })
            {
                var pane = window.GetVisualDescendants().OfType<Control>()
                    .Single(c => c.GetType().Name == name && c.IsEffectivelyVisible);
                var origin = pane.TranslatePoint(default, window)!.Value;
                var region = new PixelRect((int)origin.X + 4, (int)origin.Y + 4,
                    Math.Max(1, (int)pane.Bounds.Width - 8), Math.Max(1, (int)pane.Bounds.Height - 8));
                var colour = MostCommon(frame!, region);
                readings.Add($"{id} {name}: {colour}");
                Console.WriteLine(readings[^1]);
                colour.Should().Be(Color.Parse(expected), $"{id}'s {name} should use its navy/grey palette");
            }
            foreach (var key in ColorKeyMapping.Table.Keys.Where(key => key.StartsWith("Theme", StringComparison.Ordinal)
                         || key.StartsWith("Dock", StringComparison.Ordinal)))
            {
                var colour = ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, ColorKeyMapping.Table[key].Key)!).Color;
                readings.Add($"{id} {key}: {colour}");
                Console.WriteLine(readings[^1]);
            }
            if (evidence is not null) File.WriteAllLines(Path.Combine(evidence, id + ".txt"), readings);
        }
        finally
        {
            window.Close();
            Application.Current!.DataTemplates.Remove(locator);
            service.Apply("Classic");
            foreach (var dictionary in dictionaries.Except(originalDictionaries).ToArray()) dictionaries.Remove(dictionary);
            Static.StartupProjectHook = previous;
        }
    }

    [AvaloniaFact]
    public void AbyssTranslationRowsRetainTheAccentAlphaLadder()
    {
        var service = new ThemeService();
        var users = Substitute.For<IUserTranslationsService>();
        users.GetOverrides(Arg.Any<string>()).Returns(new Dictionary<string, string>());
        var localization = new LocalizationService(users);
        localization.Apply("en");
        var view = new TranslationEditorView
        {
            DataContext = new TranslationEditorViewModel(localization, users, Substitute.For<IWindowManager>())
        };
        var window = new Window { Content = view, Width = 1000, Height = 600 };
        try
        {
            service.Apply("Abyss");
            window.Show();
            var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
            grid.SelectedIndex = -1;
            window.UpdateLayout();
            var row = grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.IsEffectivelyVisible);
            Color RowColour()
            {
                using var frame = window.CaptureRenderedFrame();
                SaveEvidence(frame!, grid.SelectedIndex < 0 ? "Abyss-grid-resting" : "Abyss-grid-selected");
                var origin = row.TranslatePoint(default, window)!.Value;
                return MostCommon(frame!, new PixelRect((int)origin.X + 4, (int)origin.Y + 4,
                    (int)row.Bounds.Width - 8, (int)row.Bounds.Height - 8));
            }
            var resting = RowColour();
            grid.SelectedItem = row.DataContext;
            grid.Focus();
            var selected = RowColour();
            Console.WriteLine($"Abyss Translation Editor resting: {resting}; selected: {selected}");
            using var assertions = new AwesomeAssertions.Execution.AssertionScope();
            resting.Should().Be(Color.Parse("#0A2134"));
            selected.Should().NotBe(resting);
            selected.Should().Be(Color.Parse("#096085"));
            foreach (var text in row.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)))
            {
                var ink = ((ISolidColorBrush)text.Foreground!).Color;
                SyntaxHighlightingTheme.ContrastRatio(ink, resting).Should().BeGreaterThanOrEqualTo(4.5);
                SyntaxHighlightingTheme.ContrastRatio(ink, selected).Should().BeGreaterThanOrEqualTo(4.5);
            }
            foreach (var (key, expected) in new[] { ("ThemeAccentBrush2", "#993674A4"),
                         ("ThemeAccentBrush3", "#663674A4"), ("ThemeAccentBrush4", "#333674A4") })
                ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, key)!).Color
                    .Should().Be(Color.Parse(expected));
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }

    [AvaloniaFact]
    public void AbyssOptionsSelectionUsesTheTranslucentAccent()
    {
        var service = new ThemeService();
        var view = new OptionsView();
        var window = new Window { Content = view, Width = 740, Height = 540 };
        try
        {
            service.Apply("Abyss");
            window.Show();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var node = new OptionsNodeViewModel("Theme");
            tree.ItemsSource = new[] { node };
            tree.SelectedItem = node;
            window.UpdateLayout();
            var item = tree.GetVisualDescendants().OfType<TreeViewItem>().Single();
            using var frame = window.CaptureRenderedFrame();
            SaveEvidence(frame!, "Abyss-options");
            var origin = item.TranslatePoint(default, window)!.Value;
            var colour = MostCommon(frame!, new PixelRect((int)origin.X + 4, (int)origin.Y + 4,
                (int)item.Bounds.Width - 8, (int)item.Bounds.Height - 8));
            Console.WriteLine($"Abyss Options selected item: {colour}");
            colour.Should().Be(Color.Parse("#0A2134"));
            var text = item.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Theme");
            SyntaxHighlightingTheme.ContrastRatio(((ISolidColorBrush)text.Foreground!).Color, colour)
                .Should().BeGreaterThanOrEqualTo(4.5);
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }

    [AvaloniaFact]
    public void DarkActiveDocumentTabKeepsSimpleBlue()
    {
        var service = new ThemeService();
        var tab = new DocumentTabStripItem { IsSelected = true, IsActive = true, Width = 200, Height = 40 };
        var window = new Window { Content = tab, Width = 220, Height = 60 };
        try
        {
            service.Apply("Dark");
            window.Show();
            using var frame = window.CaptureRenderedFrame();
            SaveEvidence(frame!, "Dark-active-tab");
            var colour = MostCommon(frame!, new PixelRect(4, 4, 190, 30));
            colour.Should().Be(Color.Parse("#007ACC"));
            ((ISolidColorBrush)tab.Foreground!).Color.Should().Be(Color.Parse("#F0F0F0"));
            ((ISolidColorBrush)window.FindResource(window.ActualThemeVariant, "DockTabSelectedForegroundBrush")!)
                .Color.Should().Be(Color.Parse("#1C97EA"));
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }

    private static void SaveEvidence(Bitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("HEXIDE_THEME_EVIDENCE");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static Color MostCommon(Bitmap bitmap, PixelRect region)
    {
        var pixels = new int[region.Width * region.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(region, handle.AddrOfPinnedObject(), pixels.Length * 4, region.Width * 4); }
        finally { handle.Free(); }
        var pixel = unchecked((uint)pixels.GroupBy(p => p).MaxBy(g => g.Count())!.Key);
        return bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromArgb((byte)(pixel >> 24), (byte)pixel, (byte)(pixel >> 8), (byte)(pixel >> 16))
            : Color.FromArgb((byte)(pixel >> 24), (byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel);
    }
}
