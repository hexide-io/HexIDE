using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools;

public partial class TypeHierarchyToolView : UserControl
{
    public TypeHierarchyToolView()
    {
        AvaloniaXamlLoader.Load(this);
        _ = new ResultsToolHeight(this);
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TypeHierarchyToolViewModel vm)
            e.Handled = vm.NavigateSelected();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is TypeHierarchyToolViewModel vm) vm.NavigateSelected();
    }
}
