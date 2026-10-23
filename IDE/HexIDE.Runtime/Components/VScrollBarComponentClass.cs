using Avalonia.Layout;

namespace HexIDE.Runtime.Components;

public class VScrollBarComponentClass : ScrollBarComponentClass
{
    public override string Name => "VScroll";
    public override string VBTypeName => "VB.VScrollBar";
    public override (int Width, int Height) AddedSize => (255, 1335);

    protected override Orientation Orientation => Orientation.Vertical;

    public static ComponentBaseClass Instance { get; } = new VScrollBarComponentClass();
}