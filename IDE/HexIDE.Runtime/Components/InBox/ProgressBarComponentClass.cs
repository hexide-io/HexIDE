using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>
/// The ProgressBar (#1082), the pilot of the in-box controls: MSComctlLib's (MSCOMCTL.OCX) and ComctlLib's (COMCTL32.OCX).
/// </summary>
/// <remarks>
/// Its members are the type libraries' (the oracle's "In-box controls: the ProgressBar"), and its Properties window's list is
/// VB6's. Value is not among them: VB6 neither shows nor saves it, so a ProgressBar starts every run empty, and its designer
/// draws it full. Orientation and Scrolling are 6.0's, and a 5.0 ProgressBar has neither (438 at run time).
/// </remarks>
public sealed class ProgressBarComponentClass : InBoxComponentClass
{
    private ProgressBarComponentClass() : base(
        [AlignProperty, TabIndexProperty, ToolTipTextProperty],
        [BorderStyleProperty, AppearanceProperty, MousePointerProperty, EnabledProperty, OLEDropModeProperty,
         SingleMinProperty, SingleMaxProperty, OrientationProperty, ScrollingProperty],
        [ClickEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent])
    {
    }

    public static ProgressBarComponentClass Instance { get; } = new();

    static ProgressBarComponentClass()
    {
        // Measured: a ProgressBar's MousePointer reads 0, ccDefault.
        MousePointerProperty.OverrideDefault<ProgressBarComponentClass>(BuiltinTypes.VBCursorType.Default);
    }

    public override string Name => "ProgressBar";

    public override string ControlClassName => "ProgressBar";

    public override bool IsAlwaysWritten(PropertyClass property) => ReferenceEquals(property, AppearanceProperty);

    private const MemberOrigin Both = MemberOrigin.Version5 | MemberOrigin.Version6;

    /// <summary>
    /// The ProgressBar's own members, as MSCOMCTL.OCX's and COMCTL32.OCX's type libraries declare them (the oracle's "In-box
    /// controls: the ProgressBar"). 6.0 added Orientation, Scrolling and Refresh. Not implemented: MouseIcon and hWnd, OLEDrag,
    /// and the OLE drag-and-drop events, which HexIDE never raises.
    /// </summary>
    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("Max", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("Min", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("MousePointer", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("MouseIcon", MemberKind.Property, Both, IsFrmProperty: true, Implemented: false),
        new("Value", MemberKind.Property, Both, IsFrmProperty: false, Implemented: true),
        new("OLEDropMode", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("Appearance", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("BorderStyle", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("Enabled", MemberKind.Property, Both, IsFrmProperty: true, Implemented: true),
        new("hWnd", MemberKind.Property, Both, IsFrmProperty: false, Implemented: false),
        new("OLEDrag", MemberKind.Method, Both, IsFrmProperty: false, Implemented: false),
        new("Orientation", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: true, Implemented: true),
        new("Scrolling", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: true, Implemented: true),
        new("Refresh", MemberKind.Method, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("MouseDown", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("MouseMove", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("MouseUp", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("Click", MemberKind.Event, Both, IsFrmProperty: false, Implemented: true),
        new("OLEStartDrag", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLEGiveFeedback", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLESetData", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLECompleteDrag", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLEDragOver", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
        new("OLEDragDrop", MemberKind.Event, Both, IsFrmProperty: false, Implemented: false),
    ];

    /// <summary>100 x 50 px (measured: VB6 wrote 2646 and 1323 for ProgressBars read without extents and not sized since).</summary>
    public override (int X, int Y) DefaultExtent => (2646, 1323);

    /// <summary>
    /// MouseIcon, carried as it was read. Where VB6 writes it among the ProgressBar's own properties isn't measured: the probe's
    /// icon didn't load. It is put after MousePointer, which it goes with.
    /// </summary>
    public override IReadOnlyList<(string Name, PropertyClass After)> CarriedOwnProperties => [("MouseIcon", MousePointerProperty)];

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var bar = new VBProgressBar
        {
            VbMin = instance.GetPropertyOrDefault(SingleMinProperty),
            VbMax = instance.GetPropertyOrDefault(SingleMaxProperty),
            Orientation = instance.GetPropertyOrDefault(OrientationProperty),
            Scrolling = instance.GetPropertyOrDefault(ScrollingProperty),
            Appearance = instance.GetPropertyOrDefault(AppearanceProperty),
            BorderStyle = instance.GetPropertyOrDefault(BorderStyleProperty),
            OLEDropMode = instance.GetPropertyOrDefault(OLEDropModeProperty),
            Library = LibraryOf(instance),
            // A Windows progress bar takes no focus, though VB6 gives it a TabIndex.
            Focusable = false,
        };
        return bar;
    }

    public override Control InstantiateForDesigner(ComponentInstance instance)
    {
        var control = Instantiate(instance);
        ((VBProgressBar)control).ShowFull = true;
        return control;
    }
}
