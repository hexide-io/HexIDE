using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

// Object model, input and toolbox default (#1093). Loaded file blocks remain preserved and read-only.
public sealed class UpDownComponentClass : InBoxComponentClass
{
    private UpDownComponentClass() : base([TabIndexProperty, TabStopProperty, ToolTipTextProperty], [],
        [ChangeEvent, UpClickEvent, DownClickEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent, GotFocusEvent, LostFocusEvent]) { }

    public static readonly EventClass ChangeEvent = new("Change");
    public static readonly EventClass UpClickEvent = new("UpClick");
    public static readonly EventClass DownClickEvent = new("DownClick");
    public static UpDownComponentClass Instance { get; } = new();
    public override string Name => "UpDown";
    public override string ControlClassName => "UpDown";
    public override RuntimeLibrary Library => ControlLibraries.MSComCtl2;
    public override (int Width, int Height) AddedSize => (255, 750);
    public override (int X, int Y) DefaultExtent => (450, 1323);
    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("Value", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Min", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Max", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Increment", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Wrap", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("BuddyControl", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("BuddyProperty", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("AutoBuddy", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("SyncBuddy", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Alignment", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Orientation", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Enabled", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("OLEDropMode", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("hWnd", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("Change", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("UpClick", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("DownClick", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("MouseDown", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("MouseMove", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("MouseUp", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("OLEStartDrag", MemberKind.Event, MemberOrigin.Version6, false, false),
        new("OLEGiveFeedback", MemberKind.Event, MemberOrigin.Version6, false, false),
        new("OLESetData", MemberKind.Event, MemberOrigin.Version6, false, false),
        new("OLECompleteDrag", MemberKind.Event, MemberOrigin.Version6, false, false),
        new("OLEDragOver", MemberKind.Event, MemberOrigin.Version6, false, false),
        new("OLEDragDrop", MemberKind.Event, MemberOrigin.Version6, false, false),
    ];

    protected override Control InstantiateInternal(ComponentInstance instance) => new VBUpDown();
}
