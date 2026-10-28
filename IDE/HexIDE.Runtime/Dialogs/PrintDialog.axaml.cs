using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HexIDE.Runtime.Dialogs;

public partial class PrintDialog : UserControl
{
    public event Action<bool>? PrintRequest;

    public PrintDialog() => InitializeComponent();

    private void OkClick(object? sender, RoutedEventArgs e) => PrintRequest?.Invoke(true);
    private void CancelClick(object? sender, RoutedEventArgs e) => PrintRequest?.Invoke(false);
}
