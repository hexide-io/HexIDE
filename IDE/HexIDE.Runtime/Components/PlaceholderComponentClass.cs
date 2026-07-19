using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HexIDE.Runtime.Components;

public class PlaceholderComponentClass : ComponentBaseClass
{
    private readonly string _typeName;

    private PlaceholderComponentClass(string typeName) : base([])
    {
        _typeName = typeName;
    }

    public override string Name =>
        _typeName.Contains('.') ? _typeName[(_typeName.LastIndexOf('.') + 1)..] : _typeName;

    public override string VBTypeName => _typeName;

    protected override Control InstantiateInternal(ComponentInstance instance) =>
        new Border
        {
            Background = new SolidColorBrush(Color.Parse("#C0C0C0")),
            BorderBrush = new SolidColorBrush(Color.Parse("#808080")),
            BorderThickness = new Avalonia.Thickness(1),
            Child = new TextBlock
            {
                Text = Name,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 9,
                Foreground = new SolidColorBrush(Colors.Black),
            }
        };

    public static PlaceholderComponentClass ForType(string typeName) => new(typeName);
}
