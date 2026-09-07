using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.LanguageServers;

public partial class LanguageServersToolView : UserControl
{
    public LanguageServersToolView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
