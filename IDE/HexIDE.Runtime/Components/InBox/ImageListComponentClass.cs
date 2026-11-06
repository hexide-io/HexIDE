using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

public sealed class ImageListComponentClass : InBoxComponentClass
{
    public static readonly PropertyClass<int> ImageWidthProperty = new("ImageWidth", "", PropertyCategory.Appearance, 0);
    public static readonly PropertyClass<int> ImageHeightProperty = new("ImageHeight", "", PropertyCategory.Appearance, 0);
    public static readonly PropertyClass<int> MaskColorProperty = new("MaskColor", "", PropertyCategory.Appearance, 12632256);
    public static readonly PropertyClass<int> ImageBackColorProperty = new("BackColor", "", PropertyCategory.Appearance, -2147483643);
    public static readonly PropertyClass<bool> UseMaskColorProperty = new("UseMaskColor", "", PropertyCategory.Appearance, true);
    internal static readonly PropertyClass<IReadOnlyList<ImageListPicture>?> PicturesProperty = new("(Images)", "", PropertyCategory.Internal);

    private ImageListComponentClass() : base([LeftProperty, TopProperty], [ImageBackColorProperty, ImageWidthProperty, ImageHeightProperty,
        MaskColorProperty, UseMaskColorProperty], [], invisible: true) { }

    public static ImageListComponentClass Instance { get; } = new();

    static ImageListComponentClass()
    {
        WidthProperty.OverrideDefault<ImageListComponentClass>(38);
        HeightProperty.OverrideDefault<ImageListComponentClass>(38);
    }
    public override string Name => "ImageList";
    public override string ControlClassName => "ImageList";
    public override bool IsVisual => false;
    public override IEnumerable<RuntimeLibrary> Libraries => [ControlLibraries.MSComctlLib];
    public override Control InstantiateForDesigner(ComponentInstance instance) => new ImageListDesignerIcon();
    public override (int Width, int Height) AddedSize => (570, 570);
    public override (int X, int Y) DefaultExtent => (1005, 1005);
    public override bool IsAlwaysWritten(PropertyClass property) => property == ImageBackColorProperty || property == MaskColorProperty;
    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("ImageWidth", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("ImageHeight", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("MaskColor", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("BackColor", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("UseMaskColor", MemberKind.Property, MemberOrigin.Version6, true, true),
        new("ListImages", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("hImageList", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("Overlay", MemberKind.Method, MemberOrigin.Version6, false, false),
    ];

    protected override Control InstantiateInternal(ComponentInstance instance) => new VBImageList(instance);
}

internal sealed record ImageListPicture(byte[] Record, string Key, string Tag);
