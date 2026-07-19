using Avalonia.Controls;
using Avalonia.Interactivity;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class FontDialog : UserControl
{
    public FontDialog()
    {
        InitializeComponent();
    }

    private void OkClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FontDialogViewModel)?.Accept();

    private void CancelClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FontDialogViewModel)?.Cancel();
}
