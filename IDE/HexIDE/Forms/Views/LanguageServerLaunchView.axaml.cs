using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HexIDE.Forms.Views;

internal partial class LanguageServerLaunchView : UserControl
{
    public LanguageServerLaunchView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        CancelButton.Focus(NavigationMethod.Tab);
    }
}
