using Avalonia.Controls;
using Avalonia.Interactivity;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class AboutDialog : UserControl
{
    public AboutDialog()
    {
        InitializeComponent();
    }

    private void OkClick(object? sender, RoutedEventArgs e) =>
        (DataContext as AboutDialogViewModel)?.Close();
}
