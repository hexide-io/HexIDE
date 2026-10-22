using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class LabelComponentClass : ComponentBaseClass
{
    public LabelComponentClass() : base([CaptionProperty,
        EnabledProperty, FontProperty, BackColorProperty,
        BackStyleProperty, AlignmentProperty, AppearanceProperty,
        AutoSizeProperty, BorderStyleProperty, ForeColorProperty,
        MousePointerProperty, RightToLeftProperty, ToolTipTextProperty,
        UseMnemonicProperty, WhatsThisHelpIdProperty,  WordWrapProperty], [ClickEvent, DblClickEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent])
    {
    }

    public override string Name => "Label";
    public override string VBTypeName => "VB.Label";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var vbFont = instance.GetPropertyOrDefault(FontProperty);
        var control = new VBLabel()
        {
            Text = instance.GetPropertyOrDefault(CaptionProperty),
            // Through the attached property, as the other controls with a font, whose handler applies it, so a symbol
            // font draws its glyphs (#1133) and a read finds the font rather than nothing (#934).
            [AttachedProperties.FontProperty] = vbFont,
            // Through the attached property, as every other control's ForeColor: a run-time assignment writes it and
            // its handler repaints, and a read finds the form's colour rather than nothing (#795).
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            BackColor = instance.GetPropertyOrDefault(BackColorProperty),
            BackStyle = instance.GetPropertyOrDefault(BackStyleProperty),
            BorderStyle = instance.GetPropertyOrDefault(BorderStyleProperty),
            Appearance = instance.GetPropertyOrDefault(AppearanceProperty),
            Alignment = instance.GetPropertyOrDefault(AlignmentProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            FlowDirection = instance.GetPropertyOrDefault(RightToLeftProperty) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            WordWrap = instance.GetPropertyOrDefault(WordWrapProperty),
            RecognizesAccessKey = instance.GetPropertyOrDefault(UseMnemonicProperty),
        };
        return control;
    }

    static LabelComponentClass()
    {
        BackStyleProperty.OverrideDefault<LabelComponentClass>(BackStyles.Opaque);
    }

    public static ComponentBaseClass Instance { get; } = new LabelComponentClass();
}