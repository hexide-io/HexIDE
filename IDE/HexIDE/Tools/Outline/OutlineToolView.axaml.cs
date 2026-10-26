using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using System.Linq;

namespace HexIDE.Tools;

public partial class OutlineToolView : UserControl
{
    public OutlineToolView() => InitializeComponent();

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        var ancestors = (e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().ToArray();
        if (ancestors is null || ancestors.OfType<Avalonia.Controls.Primitives.ToggleButton>().Any()) return;
        var item = ancestors.OfType<TreeViewItem>().FirstOrDefault();
        if (item?.DataContext is OutlineNode node && DataContext is OutlineToolViewModel vm)
            vm.Navigate(node);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is OutlineToolViewModel { SelectedNode: { } node } vm)
        {
            vm.Navigate(node);
            e.Handled = true;
        }
    }
}
