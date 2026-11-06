using Avalonia.Controls;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Components.InBox;

// The measured object model and text-only file loader (#1085).
public sealed class ListViewComponentClass : InBoxComponentClass
{
    private ListViewComponentClass() : base([], [],
        [KeyDownEvent, KeyUpEvent, new EventClass("ItemClick", new EventClassArgument("Item", "ListItem")),
         new EventClass("ItemCheck", new EventClassArgument("Item", "ListItem")),
         new EventClass("ColumnClick", new EventClassArgument("ColumnHeader", "ColumnHeader"))]) { }
    public static ListViewComponentClass Instance { get; } = new();
    internal static readonly PropertyClass<VBSerializedComponent?> FilePropertiesProperty =
        new("(ListViewFile)", "The measured file properties, retained alongside their raw lines.", PropertyCategory.Internal);
    public override string Name => "ListView";
    public override string ControlClassName => "ListView";
    public override IEnumerable<RuntimeLibrary> Libraries => [ControlLibraries.MSComctlLib];
    public override (int X, int Y) DefaultExtent => (2646, 1323);
    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        new("Arrange", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("ColumnHeaders", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("DropHighlight", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("HideColumnHeaders", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("HideSelection", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("Icons", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("ListItems", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("LabelEdit", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("LabelWrap", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("MouseIcon", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("MousePointer", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("MultiSelect", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("SelectedItem", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("SmallIcons", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("Sorted", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("SortKey", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("SortOrder", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("View", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("OLEDragMode", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("OLEDropMode", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("Appearance", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("BackColor", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("BorderStyle", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("Enabled", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("Font", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("ForeColor", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("hWnd", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("FindItem", MemberKind.Method, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("GetFirstVisible", MemberKind.Method, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("HitTest", MemberKind.Method, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("StartLabelEdit", MemberKind.Method, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("Refresh", MemberKind.Method, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("AllowColumnReorder", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("Checkboxes", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("FlatScrollBar", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("FullRowSelect", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("GridLines", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("HotTracking", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("HoverSelection", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("Picture", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("PictureAlignment", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("ColumnHeaderIcons", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("TextBackground", MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("BeforeLabelEdit", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("AfterLabelEdit", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("ColumnClick", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("ItemClick", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("ItemCheck", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("KeyDown", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("KeyUp", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("KeyPress", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("MouseDown", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("MouseMove", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("MouseUp", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("Click", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("DblClick", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: true),
        new("OLEStartDrag", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("OLEGiveFeedback", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("OLESetData", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("OLECompleteDrag", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("OLEDragOver", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
        new("OLEDragDrop", MemberKind.Event, MemberOrigin.Version6, IsFrmProperty: false, Implemented: false),
    ];

    public override Control InstantiateForDesigner(ComponentInstance instance)
    {
        // VB6 shows the name and optional box, never report headers, in the designer (#1085).
        var name = instance.GetPropertyOrDefault(VBProperties.NameProperty) ?? "";
        var fields = instance.GetPropertyOrDefault(FilePropertiesProperty)?.Properties;
        Control label = fields is not null && fields.TryGetValue("Checkboxes", out var flag) && flag is -1
            ? new CheckBox { Content = name, IsChecked = false, IsHitTestVisible = false }
            : new TextBlock { Text = name };
        return new Border { Child = label, ClipToBounds = true };
    }

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        if (LibraryOf(instance) != ControlLibraries.MSComctlLib)
            throw new NotImplementedException("ComctlLib.ListView is unmeasured (#1085)");
        var control = new VBListView();
        control.LoadProperties(instance);
        return control;
    }
}
