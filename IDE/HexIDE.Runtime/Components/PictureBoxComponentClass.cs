using System.Diagnostics.CodeAnalysis;
using HexIDE.Runtime.BuiltinTypes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Classic.Avalonia.Theme;
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
        var (borderStyle, inset) = ClientBorder(
            instance.GetPropertyOrDefault(BorderStyleProperty),
            instance.GetPropertyOrDefault(AppearanceProperty));

        var box = new VBPictureBox()
        {
            ChildHost = CreateChildHost(),
            ClientBorderStyle = borderStyle,
            ClientInset = inset,
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

    /// <summary>
    /// VB6's <c>BorderStyle</c> + <c>Appearance</c> as the border the decorator draws, and — the same
    /// number — the inset a contained control is laid out at.
    ///
    /// Measured with <c>GetWindowRect</c> against a compiled VB6 binary: a default 3-D bordered PictureBox
    /// insets its children by exactly (2,2) pixels, 30 twips; a flat borderless one by (0,0). The general
    /// rule <c>(Width - ScaleWidth) / 2</c> agrees — <c>Tip of the Day.frm</c>'s Picture1 is Width 3735 and
    /// ScaleWidth 3675, a 60-twip difference, 2 px per side — but it is used as a test assertion rather than
    /// as the implementation, because <c>Scale*</c> is not modelled and where <c>ScaleMode</c> is not twips
    /// the subtraction is not even dimensionally meaningful.
    ///
    /// The flat-but-bordered combination was NOT measured; VB6 draws a one-pixel line for it, so one pixel
    /// is what it insets by here. Keeping the drawn border and the inset the same number is what makes that
    /// a coherent guess rather than an arbitrary one, and the oracle question is on the change's open list.
    /// </summary>
    internal static (ClassicBorderStyle Style, Thickness Inset) ClientBorder(VBBorder border, VBAppearance appearance) =>
        (border, appearance) switch
        {
            (VBBorder.FixedSingle, VBAppearance._3D) => (ClassicBorderStyle.Sunken, new Thickness(2)),
            (VBBorder.FixedSingle, _) => (ClassicBorderStyle.Thin, new Thickness(1)),
            _ => (ClassicBorderStyle.None, new Thickness(0)),
        };

    public override bool TryGetChildHost(Control control, [NotNullWhen(true)] out Canvas? host)
    {
        host = (control as VBPictureBox)?.ChildHost;
        return host is not null;
    }

    public override Thickness ClientInset(ComponentInstance instance) =>
        ClientBorder(instance.GetPropertyOrDefault(BorderStyleProperty),
                     instance.GetPropertyOrDefault(AppearanceProperty)).Inset;

    static PictureBoxComponentClass()
    {
        // VB6's PictureBox defaults to 1 - Fixed Single, and VB6 omits default-valued properties from the
        // .frm — so a PictureBox with no BorderStyle line is bordered, not borderless. HexIDE's shared
        // default is None, which made Tip of the Day.frm's Picture1 read as borderless and inset its
        // children by zero where VB6 insets by two pixels.
        //
        // One visible consequence, so it is not mistaken for a rendering regression: the property grid reads
        // GetBoxedPropertyOrDefault, so every PictureBox with no BorderStyle line now shows
        // "1 - Fixed Single" — which is what VB6 shows.
        BorderStyleProperty.OverrideDefault<PictureBoxComponentClass>(VBBorder.FixedSingle);
    }

    public static ComponentBaseClass Instance { get; } = new PictureBoxComponentClass();
}