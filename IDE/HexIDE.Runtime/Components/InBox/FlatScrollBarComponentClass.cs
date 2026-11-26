using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

// Saved own properties stay verbatim, as for RichTextBox (#1097).
public sealed class FlatScrollBarComponentClass : InBoxComponentClass, IDesignerAddedPlacement
{
    private FlatScrollBarComponentClass() : base([TabIndexProperty, ToolTipTextProperty], [],
        [ScrollBarComponentClass.ChangeEvent, ScrollEvent]) { }

    public static readonly EventClass ScrollEvent = new("Scroll");
    public static FlatScrollBarComponentClass Instance { get; } = new();
    public override string Name => "FlatScrollBar";
    public override string ControlClassName => "FlatScrollBar";
    public override RuntimeLibrary Library => ControlLibraries.MSComCtl2;
    public override (int Width, int Height) AddedSize => (1500, 255);

    /// <summary>
    /// VB6 centres the 1500 x 255 control as if it were 750 twips high (#1097). It's worked in whole twips, the form's
    /// half-width and half-height truncated, as the designer worked it before this moved here (#5681).
    /// </summary>
    public DesignerBounds? AddedBounds(double formWidth, double formHeight) =>
        new DesignerBounds(Pixels(Twips(formWidth) / 2 - 750), Pixels(Twips(formHeight) / 2 - 375), 100, 17);

    private static long Twips(double pixels) => (long)Math.Round(pixels * VBScaleModeExtensions.PixelToTwips);

    private static double Pixels(long twips) => twips / (double)VBScaleModeExtensions.PixelToTwips;
    public override (int X, int Y) DefaultExtent => (2646, 450);
    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("Value", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Min", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Max", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("SmallChange", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("LargeChange", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Orientation", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Appearance", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Arrows", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("Enabled", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("MousePointer", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("MouseIcon", MemberKind.Property, MemberOrigin.Version6, true, false),
        new("hWnd", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("Refresh", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("Change", MemberKind.Event, MemberOrigin.Version6, false, true),
        new("Scroll", MemberKind.Event, MemberOrigin.Version6, false, true),
    ];

    public override IReadOnlyList<string> CarriedPropertyOrder =>
        ["MousePointer", "Enabled", "Appearance", "Arrows", "LargeChange", "Min", "Max", "Orientation", "SmallChange", "Value"];

    internal static readonly PropertyClass<VBSerializedComponent?> SavedProperty =
        new("(FlatScrollBarSaved)", "", PropertyCategory.Internal);

    internal static bool CanModel(VBSerializedComponent saved) => saved.SubComponents.Count == 0
        && saved.Properties.Keys.All(name => Instance.Properties.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            || Instance.CarriedPropertyOrder.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name is "_ExtentX" or "_ExtentY" or "_Version");

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var bar = new VBFlatScrollBar();
        bar.LoadProperties(CarriedPropertyOrder
            .Where(name => VBLoader.RawDouble(instance, name) is not null)
            .Select(name => (name, new Vb6Value(VBLoader.RawDouble(instance, name)!.Value))).ToList());
        return bar;
    }
}
