using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.ProtocolInspector;

public partial class ProtocolInspectorToolView : UserControl
{
    public ProtocolInspectorToolView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
