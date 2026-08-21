using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class ComboBoxComponentClass : ComponentBaseClass
{
    public ComboBoxComponentClass() : base([EnabledProperty,
        FontProperty,
        ForeColorProperty,
        BackColorProperty,
        ListProperty,
        ItemDataProperty,
        LockedProperty,
        MousePointerProperty,
        RightToLeftProperty,
        AppearanceProperty,
        TabStopProperty,
        TabIndexProperty])
    {
    }

    public override string Name => "Combo";
    public override string VBTypeName => "VB.ComboBox";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBComboBox()
        {
            // No ItemsSource. List is the companion record verbatim, and turning it into items needs a
            // non-empty example to decode against — every List record in VB6's Template tree is an empty
            // two-byte count. Binding it directly would compile and be wrong: byte[] IS an IEnumerable, so
            // a populated list would render as a column of raw byte values.
            //
            // This renders empty, exactly as it did before: nothing ever populated the old strings model
            // from a file either.
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            FlowDirection = instance.GetPropertyOrDefault(RightToLeftProperty) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
    }

    static ComboBoxComponentClass()
    {
        BackColorProperty.OverrideDefault<ComboBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Window));
    }

    public static ComponentBaseClass Instance { get; } = new ComboBoxComponentClass();
}