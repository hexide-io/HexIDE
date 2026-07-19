using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HexIDE.IDE;

public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is IDialog dialog)
        {
            dialog.CloseRequested += CloseRequested;
        }
    }

    private void CloseRequested(bool dialogResult)
    {
        this.Close(dialogResult);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (DataContext is IDialog dialog)
        {
            dialog.CloseRequested -= CloseRequested;
        }
    }
}