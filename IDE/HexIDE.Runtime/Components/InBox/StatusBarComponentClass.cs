using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

// Measured on vb6.exe SP6: #1087, issuecomment-6427372779. Drawing and pointer events follow the same console measurements.
public sealed class StatusBarComponentClass : InBoxComponentClass
{
    private StatusBarComponentClass() : base(
        [AlignProperty, TabIndexProperty, ToolTipTextProperty],
        [EnabledProperty, FontProperty, StyleProperty, SimpleTextProperty, ShowTipsProperty, MousePointerProperty, OLEDropModeProperty],
        [ClickEvent, DblClickEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent, PanelClickEvent, PanelDblClickEvent]) { }

    public static readonly EventClass PanelClickEvent = new("PanelClick", new EventClassArgument("Panel", "MSComctlLib.Panel"));
    public static readonly EventClass PanelDblClickEvent = new("PanelDblClick", new EventClassArgument("Panel", "MSComctlLib.Panel"));

    public static readonly PropertyClass<int> StyleProperty = new("Style", "How the control is drawn and how it behaves.", PropertyCategory.Appearance);
    public static readonly PropertyClass<string> SimpleTextProperty = new("SimpleText", "The text displayed in simple style.", PropertyCategory.Appearance, "");
    public static readonly PropertyClass<bool> ShowTipsProperty = new("ShowTips", "Whether panel tooltips are shown.", PropertyCategory.Behavior, true);

    internal static readonly PropertyClass<Serialization.VBSerializedComponent?> SavedPanelsProperty =
        new("(SavedPanels)", "The saved panel property bag.", PropertyCategory.Internal);
    internal static readonly PropertyClass<IReadOnlyDictionary<int, byte[]>?> PanelPicturesProperty =
        new("(PanelPictures)", "The saved panel picture records.", PropertyCategory.Internal);

    public static StatusBarComponentClass Instance { get; } = new();

    static StatusBarComponentClass()
    {
        AlignProperty.OverrideDefault<StatusBarComponentClass>(BuiltinTypes.VBAlign.vbAlignBottom);
        HeightProperty.OverrideDefault<StatusBarComponentClass>(25);
        MousePointerProperty.OverrideDefault<StatusBarComponentClass>(BuiltinTypes.VBCursorType.Default);
    }

    public override string Name => "StatusBar";
    public override string ControlClassName => "StatusBar";
    // Only 6.0 was measured for this control.
    public override IEnumerable<RuntimeLibrary> Libraries => [ControlLibraries.MSComctlLib];
    public override (int Width, int Height) AddedSize => (1215, 375);
    public override (int X, int Y) DefaultExtent => throw new NotImplementedException("StatusBar file persistence is deferred to #1087 part C");

    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("Panels", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("Style", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("SimpleText", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("ShowTips", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Enabled", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Font", MemberKind.Property, MemberOrigin.Version6, true, false),
        new("MousePointer", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("OLEDropMode", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("MouseIcon", MemberKind.Property, MemberOrigin.Version6, true, false),
        new("hWnd", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("Refresh", MemberKind.Method, MemberOrigin.Version6, false, true),
        new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, false, false),
        .. new[] { "Click", "DblClick", "MouseDown", "MouseMove", "MouseUp", "PanelClick", "PanelDblClick" }
            .Select(name => new InBoxMember(name, MemberKind.Event, MemberOrigin.Version6, false, true)),
        .. new[] { "OLEStartDrag", "OLEGiveFeedback", "OLESetData", "OLECompleteDrag", "OLEDragOver", "OLEDragDrop" }
            .Select(name => new InBoxMember(name, MemberKind.Event, MemberOrigin.Version6, false, false)),
    ];

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var bar = new VBStatusBar
        {
            Focusable = false,
            ContainerHeight = instance.Container?.GetPropertyOrDefault(HeightProperty) ?? double.NaN,
            Align = (short)instance.GetPropertyOrDefault(AlignProperty),
            Style = instance.GetPropertyOrDefault(StyleProperty),
            SimpleText = instance.GetPropertyOrDefault(SimpleTextProperty) ?? "",
            ShowTips = instance.GetPropertyOrDefault(ShowTipsProperty),
            MousePointer = (long)instance.GetPropertyOrDefault(MousePointerProperty),
            OLEDropMode = (long)instance.GetPropertyOrDefault(OLEDropModeProperty),
        };
        if (instance.GetPropertyOrDefault(SavedPanelsProperty) is { } saved)
            bar.Panels.Load(saved.Properties, instance.GetPropertyOrDefault(PanelPicturesProperty));
        AttachedProperties.SetFont(bar, instance.GetPropertyOrDefault(FontProperty));
        return bar;
    }
}
