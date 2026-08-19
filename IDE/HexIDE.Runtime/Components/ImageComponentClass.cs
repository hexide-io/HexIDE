using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

/// <summary>
/// VB6's <c>Image</c> control — a windowless picture holder. It is not a <c>PictureBox</c>: it has no
/// drawing methods, cannot contain other controls, and exists only to show a picture.
///
/// Modelled primarily so forms carrying one can be SAVED. Until this existed, VB.Image was an unknown
/// component type: its whole <c>Begin</c> block was preserved as raw text and its <c>.frx</c> reference
/// stripped out with it, which held both <c>Splash Screen.frm</c> and
/// <c>Treeview Listview Splitter.frm</c> read-only. Those two are the corpus cases, and between them they
/// use every property declared here.
/// </summary>
public class ImageComponentClass : ComponentBaseClass
{
    public ImageComponentClass() : base([
        PictureProperty,
        StretchProperty,
        BorderStyleProperty,
        // MouseIcon rides with MousePointer: VB6 writes `MousePointer = 99 'Custom` beside a MouseIcon
        // citing the companion, and Treeview Listview Splitter.frm does exactly that for its splitter bar.
        MousePointerProperty,
        MouseIconProperty,
        DragIconProperty,
        EnabledProperty,
        // Name, Left, Top, Width, Height, Visible and Tag come from ComponentBaseClass — listing any of
        // them here again throws at static-init time, because PropertiesByName is a plain ToDictionary.
        ToolTipTextProperty])
    {
    }

    public override string Name => "Image";
    public override string VBTypeName => "VB.Image";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBImage()
        {
            Picture = instance.GetPropertyOrDefault(PictureProperty),
            Stretch = instance.GetPropertyOrDefault(StretchProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            IsEnabled = instance.GetPropertyOrDefault(EnabledProperty),
            IsVisible = instance.GetPropertyOrDefault(VisibleProperty),
        };
    }

    public static ComponentBaseClass Instance { get; } = new ImageComponentClass();
}
