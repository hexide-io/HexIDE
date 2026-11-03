using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Localization;
using HexIDE.Lsp.Messages;
using HexIDE.Tools.Diagnostics;

namespace HexIDE.Integration.Tests.Views;

public class DiagnosticsPresentationTests
{
    [AvaloniaFact]
    public void DocumentCellKeepsTheFullPathInItsTooltip()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var view = new DiagnosticsToolView();
        var grid = (DataGrid)((Grid)view.Content!).Children[1];
        grid.ItemsSource = new[] { new DiagnosticRow("file:///outside/a.txt", "s",
            new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 1)), "x"), "Error") };
        var window = new Window { Content = view, Width = 1000, Height = 400 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cell = view.GetVisualDescendants().OfType<DataGridRow>().Single()
                .GetVisualDescendants().OfType<DataGridCell>().ElementAt(2);
            cell.GetVisualDescendants().OfType<TextBlock>()
                .Should().ContainSingle(t => ToolTip.GetTip(t) as string == new System.Uri("file:///outside/a.txt").LocalPath);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SeverityHeaderHasItsOwnCaption()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var view = new DiagnosticsToolView();
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var grid = (DataGrid)((Grid)view.Content!).Children[1];
            grid.Columns[0].Header.Should().Be("Severity");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ServerCellFitsALongNameArrivingAfterTheWindowOpens()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var view = new DiagnosticsToolView();
        var grid = (DataGrid)((Grid)view.Content!).Children[1];
        var window = new Window { Content = view, Width = 1000, Height = 400 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            const string server = "HexIDE VB6 Language Server";
            grid.ItemsSource = new[] { new DiagnosticRow("file:///outside/a.txt", server,
                new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 1)), "x"), "Error") };
            foreach (var language in new[] { "en", "hu", "en" })
            {
                localization.Apply(language);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var cell = view.GetVisualDescendants().OfType<DataGridRow>().Single()
                    .GetVisualDescendants().OfType<DataGridCell>().ElementAt(1);
                var text = cell.GetVisualDescendants().OfType<TextBlock>().Single();
                text.Text.Should().Be(server);
                var measure = new TextBlock { Text = server, FontFamily = text.FontFamily,
                    FontSize = text.FontSize, FontWeight = text.FontWeight };
                measure.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
                text.Bounds.Width.Should().BeGreaterThanOrEqualTo(measure.DesiredSize.Width, language);
                var header = view.GetVisualDescendants().OfType<DataGridColumnHeader>()
                    .Single(h => Equals(h.Content, localization.GetString("Str.Tool.Diagnostics.Server")));
                var caption = new TextBlock { Text = (string)header.Content!, FontFamily = header.FontFamily,
                    FontSize = header.FontSize, FontWeight = header.FontWeight };
                caption.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
                header.Bounds.Width.Should().BeGreaterThanOrEqualTo(
                    caption.DesiredSize.Width + header.Padding.Left + header.Padding.Right, language);
            }
        }
        finally
        {
            window.Close();
            localization.Apply("en");
        }
    }

    [AvaloniaFact]
    public void LocalizedHeadersFitEvenWhenTheCellsAreShort()
    {
        var localization = new LocalizationService();
        var view = new DiagnosticsToolView();
        var grid = (DataGrid)((Grid)view.Content!).Children[1];
        grid.ItemsSource = new[] { new DiagnosticRow("file:///outside/a.txt", "s",
            new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 1)), "x"), "E") };
        var window = new Window { Content = view, Width = 350, Height = 400 };
        try
        {
            window.Show();
            foreach (var language in localization.AvailableLanguages.Where(l => l.Id != "system"))
            {
                localization.Apply(language.Id);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var headers = view.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is string).ToArray();
                headers.Should().HaveCount(5);
                foreach (var header in headers)
                {
                    var text = new TextBlock { Text = (string)header.Content!, FontFamily = header.FontFamily,
                        FontSize = header.FontSize, FontWeight = header.FontWeight };
                    text.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
                    header.Bounds.Width.Should().BeGreaterThanOrEqualTo(text.DesiredSize.Width + header.Padding.Left + header.Padding.Right,
                        $"{language.Id}: {header.Content}");
                }
            }
        }
        finally
        {
            window.Close();
            localization.Apply("en");
        }
    }
}
