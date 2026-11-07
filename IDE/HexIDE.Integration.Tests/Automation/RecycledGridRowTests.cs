using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

public class RecycledGridRowTests
{
    private sealed record Row(string Name);

    [AvaloniaFact]
    public void A_filtered_grid_lists_its_remaining_row_once()
    {
        var remaining = new Row("Failure");
        var items = new ObservableCollection<Row> { new("Connecting"), remaining };
        var grid = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = true,
        };
        var window = new Window { Content = grid, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            grid.GetVisualDescendants().OfType<DataGridRow>().Count().Should().Be(2);
            items.Clear();
            items.Add(remaining);
            grid.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            grid.GetVisualDescendants().OfType<DataGridRow>().Select(r => r.IsVisible).Should().Contain(false,
                "the presenter must retain a recycled row for this to exercise the defect");

            static IEnumerable<UiNode> Flat(UiNode n) => n.Children.SelectMany(Flat).Prepend(n);
            var dump = Flat(UiAutomationDriver.Dump(window, "Window", 12, false)).ToList();
            var row = dump.Where(n => n.ControlType == "DataItem").Should().ContainSingle().Subject;
            row.IsHidden.Should().NotBe(true);
            UiAutomationDriver.Resolve(window, row.Path).control.Should().BeOfType<DataGridRow>()
                .Which.DataContext.Should().BeSameAs(remaining);
            UiAutomationDriver.Resolve(window, "Window/DataGrid/DataItem[#1]").control.Should().BeNull();

            var current = grid.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.IsVisible);
            current.IsVisible = false;
            Flat(UiAutomationDriver.Dump(window, "Window", 12, false)).Where(n => n.ControlType == "DataItem")
                .Should().ContainSingle().Which.IsHidden.Should().Be(true, "a current hidden row is still listed");
        }
        finally { window.Close(); }
    }
}
