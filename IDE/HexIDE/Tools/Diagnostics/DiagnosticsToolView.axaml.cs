using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.Diagnostics;

public partial class DiagnosticsToolView : UserControl
{
    public DiagnosticsToolView() => AvaloniaXamlLoader.Load(this);

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
