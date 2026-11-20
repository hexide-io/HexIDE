using System.Linq;
using System.Collections.Generic;
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
        MultiSelectProperty,
        // Same reason as CommandButton.Picture: held so the blob survives, not because dragging draws it.
        DragIconProperty,
        MousePointerProperty,
        RightToLeftProperty,
        AppearanceProperty,
        TabStopProperty,
        TabIndexProperty,
        ToolTipTextProperty], [ClickEvent, ItemCheckEvent, ScrollEvent, DblClickEvent, GotFocusEvent, LostFocusEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent, KeyDownEvent, KeyPressEvent, KeyUpEvent])
    {
        Members = CreateMembers();
    }

    internal static readonly EventClass ItemCheckEvent = new("ItemCheck", new EventClassArgument("Item", "Integer"));

    public static readonly EventClass ScrollEvent = new("Scroll");

    // Existing designer and runtime surface; extensions are code-only (#1116).
    public override IReadOnlyList<InBox.InBoxMember> Members { get; }

    private IReadOnlyList<InBox.InBoxMember> CreateMembers() =>
    [
        .. Properties.Select(p => new InBox.InBoxMember(p.Name.Trim('(', ')'), InBox.MemberKind.Property, InBox.MemberOrigin.VB6, true, true)),
        .. Events.Select(e => new InBox.InBoxMember(e.Name, InBox.MemberKind.Event, InBox.MemberOrigin.VB6, false, true)),
        .. new[] { "ListIndex", "ListCount", "NewIndex", "Text", "Selected", "SelCount", "TopIndex", "Style", "Columns" }
            .Select(n => new InBox.InBoxMember(n, InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true)),
        .. new[] { "FontName", "FontSize", "FontBold", "FontItalic", "FontUnderline", "FontStrikethru", "Index" }
            .Select(n => new InBox.InBoxMember(n, InBox.MemberKind.Property, InBox.MemberOrigin.VB6, false, true)),
        .. new[] { "AddItem", "RemoveItem", "Clear", "Move", "ZOrder", "SetFocus" }
            .Select(n => new InBox.InBoxMember(n, InBox.MemberKind.Method, InBox.MemberOrigin.VB6, false, true)),
        .. new[] { "FindString", "FindStringExact" }
            .Select(n => new InBox.InBoxMember(n, InBox.MemberKind.Method, InBox.MemberOrigin.HexIde, false, true)),
    ];

    public override string? DesignerDoubleClickEvent => "Click";

    public override string Name => "List";
    public override string VBTypeName => "VB.ListBox";
    public override (int Width, int Height) AddedSize => (1215, 450);

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var control = new VBListBox()
        {
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(BackColorProperty),
            [AttachedProperties.ForeColorProperty] = instance.GetPropertyOrDefault(ForeColorProperty),
            [AttachedProperties.FontProperty] = instance.GetPropertyOrDefault(FontProperty),
            Cursor = instance.GetPropertyOrDefault(MousePointerProperty).ToCursor(),
            FlowDirection = instance.GetPropertyOrDefault(RightToLeftProperty) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
            MultiSelect = instance.GetPropertyOrDefault(MultiSelectProperty),
            SavedStyle = VBLoader.RawNumber(instance, "Style") ?? 0,
            Columns = VBLoader.RawNumber(instance, "Columns") ?? 0,
            Sorted = VBLoader.RawDouble(instance, "Sorted") is { } sorted && sorted != 0
        };
        AvaloniaInterop.ListItemState.Load(control, instance);
        return control;
    }

    // These properties stay carried verbatim; designer drawing is outside #4209's measurements.
    public override Control InstantiateForDesigner(ComponentInstance instance)
    {
        var list = (VBListBox)Instantiate(instance);
        list.UsePlainDesignerLayout();
        // The preview drew saved items as a garbled glyph, so it stays empty, as it was before they loaded (#5121).
        list.Items.Clear();
        return list;
    }

    static ListBoxComponentClass()
    {
        BackColorProperty.OverrideDefault<ListBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Window));
        // The window's text colour, not the button text every other control defaults to (measured: -2147483640).
        ForeColorProperty.OverrideDefault<ListBoxComponentClass>(VBColor.FromSystemColor(VbSystemColor.Windowtext));
    }

    public static ComponentBaseClass Instance { get; } = new ListBoxComponentClass();
}
