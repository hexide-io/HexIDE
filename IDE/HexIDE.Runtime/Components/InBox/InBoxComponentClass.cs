using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>
/// A control of one of VB6's in-box ActiveX libraries, re-implemented (#1082). One class serves every library that
/// declares the control: MSComctlLib's ProgressBar and ComctlLib's are one implementation, registered under both names,
/// and an instance keeps the name it was read under so it is written back under it.
/// </summary>
/// <remarks>
/// VB6 writes such a control's block in two runs (measured): first its extender's properties, by name as an intrinsic's
/// are, then <c>_ExtentX</c>, <c>_ExtentY</c> and <c>_Version</c>, then the control's own properties in the control's own
/// fixed order, each only where it differs from the control's default, and with a comment only on a Boolean.
/// </remarks>
public abstract class InBoxComponentClass : ComponentBaseClass
{
    /// <param name="extender">The extender's properties beyond the base's Name, Left, Top, Width, Height, Visible and Tag.</param>
    /// <param name="own">The control's own persisted properties, in the order it writes them.</param>
    protected InBoxComponentClass(IReadOnlyList<PropertyClass> extender, IReadOnlyList<PropertyClass> own, IReadOnlyList<EventClass> events, bool invisible = false)
        : base([.. extender, .. own], events, hasBounds: !invisible, hasVisible: !invisible)
    {
        OwnProperties = own;
    }

    /// <summary>The in-box controls HexIDE implements (#1082), which the toolbox offers from each library declaring one.</summary>
    public static IReadOnlyList<InBoxComponentClass> Implemented =>
        [ProgressBarComponentClass.Instance, StatusBarComponentClass.Instance, ImageListComponentClass.Instance, SSTabComponentClass.Instance,
            CommonDialogComponentClass.Instance, RichTextBoxComponentClass.Instance, UpDownComponentClass.Instance];

    /// <summary>The control's class name, as its libraries declare it: <c>ProgressBar</c>.</summary>
    public abstract string ControlClassName { get; }

    /// <summary>The newest library that declares it, whose type name a new one takes.</summary>
    public virtual RuntimeLibrary Library => ControlLibraries.MSComctlLib;

    public override string VBTypeName => Library.TypeName(ControlClassName);

    /// <summary>Every library under whose type name HexIDE implements the control.</summary>
    public virtual IEnumerable<RuntimeLibrary> Libraries =>
        ControlLibraries.All.Where(l => l.Controls.ContainsKey(ControlClassName));

    /// <summary>The control's own persisted properties, in the order it writes them.</summary>
    public IReadOnlyList<PropertyClass> OwnProperties { get; }

    /// <summary>
    /// The control's own members, as its type libraries declare them, with where each comes from (#1082). The hidden ones
    /// are left out: VB6 offers neither to a program's completion nor its Object Browser.
    /// </summary>
    public abstract IReadOnlyList<InBoxMember> Members { get; }

    /// <summary>The member named <paramref name="name"/>, in any case, or null.</summary>
    public InBoxMember? Member(string name) =>
        Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>An own property written whatever its value: a ProgressBar's Appearance (measured).</summary>
    public virtual bool IsAlwaysWritten(PropertyClass property) => false;

    /// <summary>
    /// Own properties HexIDE carries without modelling them, by name, and the own property each is written after: a
    /// ProgressBar's MouseIcon. Carried lines with any other name are the extender's, and sort with its run.
    /// </summary>
    public virtual IReadOnlyList<(string Name, PropertyClass After)> CarriedOwnProperties => [];

    /// <summary>Own scalar lines carried verbatim, in their measured file order.</summary>
    public virtual IReadOnlyList<string> CarriedPropertyOrder => [];

    /// <summary>Whether a carried own scalar line differs from its measured default.</summary>
    public virtual bool WriteCarriedProperty(ComponentInstance.UnknownRawProperty property) => true;

    /// <summary>The type name <paramref name="instance"/> is written under: the one it was read under, or this class's.</summary>
    public string TypeNameOf(ComponentInstance instance) =>
        instance.GetPropertyOrDefault(LoadedTypeNameProperty) ?? VBTypeName;

    /// <summary>The library <paramref name="instance"/> is in, by the type name it is written under.</summary>
    public RuntimeLibrary LibraryOf(ComponentInstance instance) =>
        ControlLibraries.ForTypeName(TypeNameOf(instance)) ?? Library;

    /// <summary>The type name the instance was read under, where it was read: <c>ComctlLib.ProgressBar</c>.</summary>
    public static readonly PropertyClass<string?> LoadedTypeNameProperty =
        new("(TypeName)", "The type name the control was read under.", PropertyCategory.Internal);

    /// <summary>The size, in pixels, the instance had when its <c>_ExtentX</c> and <c>_ExtentY</c> were read.</summary>
    public static readonly PropertyClass<ExtentBasis?> ExtentBasisProperty =
        new("(ExtentBasis)", "The size the control's extents were read beside.", PropertyCategory.Internal);

    /// <summary>A size in pixels, as the extents were read beside it.</summary>
    public sealed record ExtentBasis(double Width, double Height);

    /// <summary>
    /// The HIMETRIC extent VB6 writes for a control <paramref name="pixels"/> across: pixels × 2540 ÷ 96, rounded half up
    /// (measured: 132 px is 3493, 177 is 4683, 180 is 4763).
    /// </summary>
    public static int Extent(int pixels) => (pixels * 2540 + 48) / 96;

    /// <summary>The extents the control has before anything sizes it, in HIMETRIC: what VB6 writes for one read without any.</summary>
    public abstract (int X, int Y) DefaultExtent { get; }

    /// <summary>
    /// The extents to write for <paramref name="instance"/>. VB6 keeps a control's extents as they are until the control is
    /// sized, then sets them from its new size (measured). So while its size is the one it was read with, it keeps the ones
    /// it was read with, even where a hand-edited file's disagree with its Width and Height, or the control's default where
    /// the file had none; once sized, or for one never read, they are its size's.
    /// </summary>
    public virtual (int X, int Y) ExtentsToWrite(ComponentInstance instance, int? readX, int? readY)
    {
        var width = instance.GetPropertyOrDefault(VBProperties.WidthProperty);
        var height = instance.GetPropertyOrDefault(VBProperties.HeightProperty);
        if (instance.GetPropertyOrDefault(ExtentBasisProperty) is { } basis
            && Math.Abs(basis.Width - width) < 1e-9 && Math.Abs(basis.Height - height) < 1e-9)
            return readX is { } x && readY is { } y ? (x, y) : DefaultExtent;
        return (Extent((int)Math.Round(width)), Extent((int)Math.Round(height)));
    }

    /// <summary>
    /// The issue that tracks the in-box control <paramref name="typeName"/> names, such as #1085 for
    /// <c>MSComctlLib.ListView</c>. Null for a type name that is no in-box library's control.
    /// </summary>
    public static int? IssueFor(string typeName)
    {
        var library = ControlLibraries.ForTypeName(typeName);
        var className = typeName[(typeName.IndexOf('.') + 1)..];
        return library is not null && library.Controls.TryGetValue(className, out var issue) ? issue : null;
    }

    /// <summary>Record what the deserializer read that the writer needs back: the type name, and the size beside the extents.</summary>
    public void Loaded(ComponentInstance instance, string typeName)
    {
        instance.SetProperty(LoadedTypeNameProperty, typeName);
        instance.SetProperty(ExtentBasisProperty, new ExtentBasis(
            instance.GetPropertyOrDefault(VBProperties.WidthProperty), instance.GetPropertyOrDefault(VBProperties.HeightProperty)));
    }
}
