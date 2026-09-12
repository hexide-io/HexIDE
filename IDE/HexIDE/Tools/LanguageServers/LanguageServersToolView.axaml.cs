using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.LanguageServers;

public partial class LanguageServersToolView : UserControl
{
    public LanguageServersToolView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Puts the whole window on the clipboard as text, pseudonymised.
    /// </summary>
    /// <remarks>
    /// Code-behind because the clipboard hangs off the <see cref="TopLevel"/>, which a view model has no
    /// business reaching for. The text itself is composed in the view model, so what gets copied is
    /// asserted on by a test and readable by an automation client, neither of which can read a clipboard.
    /// </remarks>
    private async void CopyReport_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LanguageServersToolViewModel vm) return;
        if (vm.ReportText is not { Length: > 0 } report) return;

        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(report) ?? Task.CompletedTask);
    }
}
