using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed partial class ImageListDesignerIcon : UserControl
{
    public ImageListDesignerIcon() => AvaloniaXamlLoader.Load(this);
}
