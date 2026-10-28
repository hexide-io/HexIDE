using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

// Object model only (#1094 part A). File blocks remain preserved until part C implements their writer.
public sealed class DTPickerComponentClass : InBoxComponentClass
{
    private DTPickerComponentClass() : base([TabIndexProperty, TabStopProperty, ToolTipTextProperty], [],
        [ChangeEvent, DropDownEvent, CloseUpEvent]) { }

    public static readonly EventClass ChangeEvent = new("Change");
    public static readonly EventClass DropDownEvent = new("DropDown");
    public static readonly EventClass CloseUpEvent = new("CloseUp");
    public static DTPickerComponentClass Instance { get; } = new();
    public override string Name => "DTPicker";
    public override string ControlClassName => "DTPicker";
    public override RuntimeLibrary Library => ControlLibraries.MSComCtl2;
    public override (int Width, int Height) AddedSize => (1500, 750);
    public override (int X, int Y) DefaultExtent => (2646, 1323);
    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("Value", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Format", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("CustomFormat", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("MinDate", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("MaxDate", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("CheckBox", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("UpDown", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Year", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Month", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Day", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Hour", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Minute", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Second", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("DayOfWeek", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Font", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("MouseIcon", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("hWnd", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("CalendarBackColor", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("CalendarForeColor", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("CalendarTitleBackColor", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("CalendarTitleForeColor", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("CalendarTrailingForeColor", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("MousePointer", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("OLEDropMode", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Change", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("DropDown", MemberKind.Event, MemberOrigin.Version6, false, false),
        new("CloseUp", MemberKind.Event, MemberOrigin.Version6, false, false),
        new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("AboutBox", MemberKind.Method, MemberOrigin.Version6, false, false),
    ];

    protected override Control InstantiateInternal(ComponentInstance instance) => new VBDTPicker();
}
