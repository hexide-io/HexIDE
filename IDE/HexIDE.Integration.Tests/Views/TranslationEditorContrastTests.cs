using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Themes;
using HexIDE.Tools.TranslationEditor;

namespace HexIDE.Integration.Tests.Views;

public class TranslationEditorContrastTests
{
    [AvaloniaTheory]
    [InlineData("Classic", false)]
    [InlineData("Dark", false)]
    [InlineData("Abyss", false)]
    [InlineData("HighContrastLight", false)]
    [InlineData("HighContrastDark", false)]
    [InlineData("Classic", true)]
    [InlineData("Dark", true)]
    [InlineData("Abyss", true)]
    [InlineData("HighContrastLight", true)]
    [InlineData("HighContrastDark", true)]
    public void FromCellInheritsSelectionAndFallbackHasNonColourCue(string pack, bool fallback)
    {
        var theme = new ThemeService();
        var view = new TranslationEditorView();
        var grid = view.GetLogicalDescendants().OfType<DataGrid>().Single();
        var ordinary = new TranslationRowViewModel("Str.Test.Ordinary") { FromValue = "Ordinary reference" };
        var missing = new TranslationRowViewModel("Str.Test.Fallback") { FromValue = "English fallback", IsRedFallback = true };
        grid.ItemsSource = new[] { ordinary, missing };
        var window = new Window { Width = 1000, Height = 500, Content = view };
        try
        {
            theme.Apply(pack);
            window.Show();
            window.UpdateLayout();
            grid.SelectedItem = fallback ? ordinary : missing;
            Dispatcher.UIThread.RunJobs();
            var target = fallback ? missing : ordinary;
            TextBlock Cell() => grid.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == target.FromValue);
            if (fallback)
            {
                Cell().FontStyle.Should().Be(FontStyle.Italic);
                Cell().Foreground.Should().Be(view.FindResource("ValidationErrorBrush"));
            }
            grid.SelectedItem = target;
            grid.Focus();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            var cell = Cell();
            var row = cell.GetVisualAncestors().OfType<DataGridRow>().Single();
            row.IsSelected.Should().BeTrue();
            var background = row.GetVisualDescendants().OfType<Rectangle>()
                .Single(r => r.Name == "BackgroundRectangle").Fill;
            var foreground = ((ISolidColorBrush)cell.Foreground!).Color;
            SyntaxHighlightingTheme.ContrastRatio(foreground, ((ISolidColorBrush)background!).Color)
                .Should().BeGreaterThanOrEqualTo(4.5, $"{pack}, fallback={fallback}: selected From text");
            cell.FontStyle.Should().Be(fallback ? FontStyle.Italic : FontStyle.Normal);
            ToolTip.SetIsOpen(cell, true);
            Dispatcher.UIThread.RunJobs();
            var tooltip = ToolTip.GetTip(cell).Should().BeOfType<StackPanel>().Subject;
            tooltip.Children.OfType<TextBlock>().Last().IsVisible.Should().Be(fallback);
            ToolTip.SetIsOpen(cell, false);
        }
        finally
        {
            window.Close();
            theme.Apply("Classic");
        }
    }
}
