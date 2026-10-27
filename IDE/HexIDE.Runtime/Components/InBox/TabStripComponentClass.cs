using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Components.InBox;

public sealed class TabStripComponentClass : InBoxComponentClass
{
    private TabStripComponentClass() : base([VBProperties.TabIndexProperty, VBProperties.TabStopProperty], [],
        [ClickEvent, BeforeClickEvent, MouseDownEvent, MouseUpEvent]) { }

    public static readonly EventClass BeforeClickEvent = new("BeforeClick", new EventClassArgument("Cancel", "Integer"));
    public static TabStripComponentClass Instance { get; } = new();
    public override IEnumerable<RuntimeLibrary> Libraries => [ControlLibraries.MSComctlLib];
    public override string Name => "TabStrip";
    public override string ControlClassName => "TabStrip";
    // The extents of a loaded block without them have not been measured.
    public override (int X, int Y) DefaultExtent => throw new NotImplementedException("TabStrip persistence is not implemented (#1089)");
    private const MemberOrigin Both = MemberOrigin.Version5 | MemberOrigin.Version6;
    private const MemberOrigin Version6 = MemberOrigin.Version6;
    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("Tabs", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("SelectedItem", MemberKind.Property, Both, IsFrmProperty: false, Implemented: true),
        new("Style", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("MultiRow", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("TabWidthStyle", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("TabFixedWidth", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("TabFixedHeight", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("TabMinWidth", MemberKind.Property, Version6, IsFrmProperty: true, Implemented: false),
        new("Placement", MemberKind.Property, Version6, IsFrmProperty: true, Implemented: true),
        new("TabStyle", MemberKind.Property, Version6, IsFrmProperty: true, Implemented: true),
        new("HotTracking", MemberKind.Property, Version6, IsFrmProperty: true, Implemented: true),
        new("MultiSelect", MemberKind.Property, Version6, IsFrmProperty: true, Implemented: true),
        new("Separators", MemberKind.Property, Version6, IsFrmProperty: true, Implemented: true),
        new("ShowTips", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("ImageList", MemberKind.Property, Both, IsFrmProperty: true, Implemented: false),
        new("ClientLeft", MemberKind.Property, Both, IsFrmProperty: false, Implemented: false),
        new("ClientTop", MemberKind.Property, Both, IsFrmProperty: false, Implemented: false),
        new("ClientWidth", MemberKind.Property, Both, IsFrmProperty: false, Implemented: false),
        new("ClientHeight", MemberKind.Property, Both, IsFrmProperty: false, Implemented: false),
        new("Font", MemberKind.Property, Both, IsFrmProperty: true, Implemented: false),
        new("Enabled", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("hWnd", MemberKind.Property, Both, IsFrmProperty: false, Implemented: false),
        new("MousePointer", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("MouseIcon", MemberKind.Property, Both, IsFrmProperty: true, Implemented: false),
        new("OLEDropMode", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("DeselectAll", MemberKind.Method, Version6, IsFrmProperty: false, Implemented: true),
        new("Refresh", MemberKind.Method, Both, IsFrmProperty: false, Implemented: false),
        new("OLEDrag", MemberKind.Method, Both, IsFrmProperty: false, Implemented: false),
        new("BeforeClick", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("Click", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("KeyDown", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("KeyPress", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("KeyUp", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("MouseDown", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("MouseMove", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("MouseUp", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("OLEStartDrag", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLEGiveFeedback", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLESetData", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLECompleteDrag", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLEDragOver", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLEDragDrop", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
    ];

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        // Saved fields outside the measured property bag must not be silently ignored.
        foreach (var property in instance.UnknownRawProperties)
            if (property.Name is not ("_ExtentX" or "_ExtentY" or "_Version") && !TabStripPersistence.OwnLine(property.Name))
                throw new NotImplementedException($"Loading TabStrip {property.Name} is not implemented (#1089)");
        var strip = new VBTabStrip();
        if (instance.GetPropertyOrDefault(TabStripPersistence.SavedProperty) is { } saved)
        {
            foreach (var name in new[] { "ImageList", "Font" })
                if (saved.Properties.ContainsKey(name))
                    throw new NotImplementedException($"Loading TabStrip {name} is not implemented (#1089)");
            TabStripPersistence.Load(strip, saved);
        }
        return strip;
    }
}
