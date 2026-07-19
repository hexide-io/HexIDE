using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace HexIDE.Forms.Views;

public partial class TrustChainView : UserControl
{
    public TrustChainView() => InitializeComponent();

    // Copy a fingerprint to the clipboard. Clipboard lives on the TopLevel, so this is code-behind rather
    // than a VM command.
    private async void CopyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string value }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(value);
        }
    }
}
