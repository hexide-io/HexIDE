using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HexIDE.Forms.ViewModels.Options;

namespace HexIDE.Forms.Views.Options;

public partial class KeymapPageView : UserControl
{
    public KeymapPageView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, CaptureKey, RoutingStrategies.Tunnel);
    }

    private void CaptureKey(object? sender, KeyEventArgs e)
    {
        if (DataContext is not KeymapPageViewModel { IsCapturing: true } page) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { page.CancelCaptureCommand.Execute(null); return; }
        if (e.Key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
        page.Capture(new KeyGesture(e.Key, e.KeyModifiers));
    }
}
