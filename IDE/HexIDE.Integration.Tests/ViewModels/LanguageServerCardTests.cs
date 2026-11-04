using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Redaction;
using HexIDE.Tools.LanguageServers;

namespace HexIDE.Integration.Tests.ViewModels;

public class LanguageServerCardTests
{
    private static LanguageServersToolView CreateView()
    {
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([new LanguageServerConnection(
            "bundled", "Bundled", LanguageConnectionKind.LanguageServer, LanguageConnectionState.Running,
            [".bas"], "vb6", null, LanguageConnectionTransport.Stdio, null, 0,
            DateTimeOffset.UtcNow.AddSeconds(-10), ReportedIdentity: new ServerIdentity("Server", "1.0"))]);
        registry.ConfigurationProblems.Returns([]);
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns("for {0}");
        return new LanguageServersToolView { DataContext = new LanguageServersToolViewModel(
            registry, loc, new ConversationLog(), Substitute.For<IEventBus>(), new Pseudonymiser()) };
    }

    [AvaloniaTheory]
    [InlineData("Reported by the server")]
    [InlineData("Vom Sprachserver gemeldeter vollständiger Name")]
    public void LabelsFitAndValuesAlign(string reportedLabel)
    {
        var view = CreateView();
        view.Resources["Str.Tool.LanguageServers.ReportedBy"] = reportedLabel;
        var window = new Window { Content = view, Width = 1000, Height = 900 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var labels = view.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("label") && t.IsEffectivelyVisible).ToArray();
            var reported = labels.Single(t => t.Text == reportedLabel);
            var natural = new TextBlock { Text = reportedLabel, FontSize = reported.FontSize, FontFamily = reported.FontFamily };
            natural.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));
            reported.Bounds.Width.Should().BeGreaterThanOrEqualTo(natural.DesiredSize.Width);
            var grids = labels.Select(t => (Grid)t.Parent!).ToArray();
            var starts = grids.Select(g => g.Children.Single(c => Grid.GetColumn(c) == 1).Bounds.X).ToArray();
            starts.Should().OnlyContain(x => Math.Abs(x - starts[0]) < 0.1);
            starts[0].Should().BeGreaterThan(reported.Bounds.Right);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AgeAdvancesOnlyWhileShown()
    {
        var view = CreateView();
        var vm = (LanguageServersToolViewModel)view.DataContext!;
        var row = vm.Groups.Single().Rows.Single();
        var changes = 0;
        row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(row.Age)) changes++; };
        var window = new Window { Content = view, Width = 1000, Height = 900 };
        window.Show();
        try
        {
            var age = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == row.Age);
            var before = age.Text;
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            changes.Should().BeGreaterThan(0);
            age.Text.Should().NotBe(before);
            view.IsVisible = false;
            var hidden = changes;
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            changes.Should().Be(hidden);
            view.IsVisible = true;
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            changes.Should().BeGreaterThan(hidden);
            window.Content = null;
            var detached = changes;
            await Task.Delay(1500, TestContext.Current.CancellationToken);
            changes.Should().Be(detached);
        }
        finally { window.Close(); }
    }
}
