using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class FontDialog : UserControl
{
    public FontDialog()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) == null) return;
            FontNameBox.Focus();
            FontNameBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    internal Func<string, Task>? ShowSizeError { get; set; }

    private async void OkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FontDialogViewModel vm) return;
        var error = vm.Accept();
        if (error == null) return;
        if (ShowSizeError != null) await ShowSizeError(error);
        FontSizeBox.Focus();
    }

    private void CancelClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FontDialogViewModel)?.Cancel();
}
