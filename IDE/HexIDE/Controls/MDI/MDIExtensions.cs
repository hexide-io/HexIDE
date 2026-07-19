using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HexIDE.Controls;

public static class MDIExtensions
{
    public static void ActivateMDIForm(this Control control)
    {
        control.RaiseEvent(new RoutedEventArgs()
        {
            Source = control,
            RoutedEvent = MDIHost.ActivateWindowEvent
        });
    }
}