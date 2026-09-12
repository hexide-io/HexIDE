using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.ProtocolInspector;

public partial class ProtocolInspectorToolView : UserControl
{
    public ProtocolInspectorToolView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Puts the selected message on the clipboard in the borrowed trace shape.
    /// </summary>
    /// <remarks>
    /// Code-behind rather than a command because the clipboard hangs off the <see cref="TopLevel"/>, which
    /// a view model has no business reaching for — the same shape the Object Browser's copy already uses.
    /// The <em>text</em> is composed in the view model, so what gets copied is asserted on by a test and
    /// readable by an automation client, neither of which can read a clipboard.
    /// </remarks>
    private async void Copy_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProtocolInspectorToolViewModel vm) return;
        if (vm.SelectedTrace is not { Length: > 0 } trace) return;

        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(trace) ?? Task.CompletedTask);
    }
}
