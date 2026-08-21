using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public class ListBoxComponentClass : ComponentBaseClass
{
    public ListBoxComponentClass() : base([EnabledProperty,
        FontProperty,
        ForeColorProperty,
        BackColorProperty,
        ListProperty,
        ItemDataProperty,
        LockedProperty,
        // Same reason as CommandButton.Picture: held so the blob survives, not because dragging draws it.
        DragIconProperty,
        MousePointerProperty,
        RightToLeftProperty,
        AppearanceProperty,
        TabStopProperty,
        TabIndexProperty], [ClickEvent])
    {
    }

    public override string Name => "List";
    public override string VBTypeName => "VB.ListBox";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        return new VBListBox()
        {
            ItemsSource = instance.GetPropertyOrDefault(ListProperty),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            FlowDirection = instance.GetPropertyOrDefault(RightToLeftProperty) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
    }

    static ListBoxComponentClass()
    {
        BackColorProperty.OverrideDefault<ListBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Window));
    }

    public static ComponentBaseClass Instance { get; } = new ListBoxComponentClass();
}