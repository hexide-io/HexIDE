using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools;

public partial class FindReferencesToolView : UserControl
{
    public FindReferencesToolView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnKeyDown, handledEventsToo: true);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FindReferencesToolViewModel vm)
            e.Handled = vm.NavigateSelected();
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FindReferencesToolViewModel vm) vm.NavigateSelected();
    }
}
