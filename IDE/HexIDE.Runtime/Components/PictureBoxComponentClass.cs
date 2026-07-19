using HexIDE.Runtime.BuiltinTypes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class PictureBoxComponentClass : ComponentBaseClass
{
    public PictureBoxComponentClass() : base([AlignProperty,
    AppearanceProperty, AutoRedrawProperty, AutoSizeProperty,
    BackColorProperty, BorderStyleProperty,
    CausesValidationProperty, EnabledProperty,
    FillColorProperty, FillStyleProperty,
    FontProperty, ForeColorProperty,
    MousePointerProperty, TabIndexProperty, TabStopProperty,
    ToolTipTextProperty, PictureProperty,
    ], [ClickEvent])
    {
    }

    public override string Name => "Picture";
    public override string VBTypeName => "VB.PictureBox";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var box = new VBPictureBox()
        {
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            FlowDirection = instance.GetPropertyOrDefault(RightToLeftProperty) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        var pictureData = instance.GetPropertyOrDefault(PictureProperty);
        if (pictureData != null)
            box.PictureData = pictureData;
        return box;
    }

    public static ComponentBaseClass Instance { get; } = new PictureBoxComponentClass();
}