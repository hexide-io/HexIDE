using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.Diagnostics;

public partial class DiagnosticsToolView : UserControl
{
    public DiagnosticsToolView()
    {
        AvaloniaXamlLoader.Load(this);
        var grid = (DataGrid)((Grid)Content!).Children[1];
        foreach (var column in grid.Columns)
            column.PropertyChanged += (_, e) =>
            {
                if (e.Property != DataGridColumn.HeaderProperty) return;
                // Auto caches measured widths; a translated header needs a fresh measurement.
                column.Width = DataGridLength.SizeToCells;
                column.Width = DataGridLength.Auto;
            };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is DiagnosticsToolViewModel vm)
            e.Handled = vm.NavigateSelected();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DiagnosticsToolViewModel vm) vm.NavigateSelected();
    }
}
