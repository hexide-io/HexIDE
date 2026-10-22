using Avalonia.Controls;

namespace HexIDE.Tools;

public partial class CallStackToolView : UserControl
{
    public CallStackToolView()
    {
        InitializeComponent();
    }

    // A row the debugger declines is put back by the view model as the list writes it, which the binding does not carry
    // back while it is writing, so the list is put back to the view model's row here, once the write is done (#1182).
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && DataContext is CallStackToolViewModel vm && !ReferenceEquals(list.SelectedItem, vm.SelectedFrame))
            list.SelectedItem = vm.SelectedFrame;
    }
}
