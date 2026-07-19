using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace HexIDE.Tools.ObjectBrowser;

public partial class ObjectBrowserToolView : UserControl
{
    public ObjectBrowserToolView()
    {
        InitializeComponent();
    }

    private void Members_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ObjectBrowserToolViewModel vm)
            vm.GoToDefinitionCommand.Execute(null);
    }

    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ObjectBrowserToolViewModel vm) return;
        var text = vm.DescriptionSignature;
        if (!string.IsNullOrEmpty(text))
            await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);
    }
}
