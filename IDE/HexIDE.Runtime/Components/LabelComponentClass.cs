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
        UseMnemonicProperty, WhatsThisHelpIdProperty,  WordWrapProperty], [ClickEvent])
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
            FontSize = vbFont.Size,
            FontFamily = vbFont.ToFontFamily(),
            FontWeight = vbFont.ToFontWeight(),
            FontStyle = vbFont.ToFontStyle(),
            Foreground = instance.GetPropertyOrDefault(ForeColorProperty).ToBrush(),
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