using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Components.InBox;

// Own file values are carried by TreeViewPersistence; they have no designer editors yet.
internal sealed class TreeViewComponentClass : ComponentBaseClass
{
    private TreeViewComponentClass() : base([VBProperties.TabIndexProperty, VBProperties.TabStopProperty],
        [ClickEvent, DblClickEvent, GotFocusEvent, LostFocusEvent, MouseDownEvent, MouseUpEvent, MouseMoveEvent,
            KeyDownEvent, KeyUpEvent,
            new EventClass("BeforeLabelEdit", new EventClassArgument("Cancel", "Integer")),
            new EventClass("AfterLabelEdit", new EventClassArgument("Cancel", "Integer"), new EventClassArgument("NewString", "String")),
            new EventClass("NodeClick", new EventClassArgument("Node", "Node")),
            new EventClass("NodeCheck", new EventClassArgument("Node", "Node")),
            new EventClass("Expand", new EventClassArgument("Node", "Node")),
            new EventClass("Collapse", new EventClassArgument("Node", "Node"))]) { }
    public static TreeViewComponentClass Instance { get; } = new();
    public override string Name => "TreeView";
    public override string VBTypeName => "MSComctlLib.TreeView";

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var tree = new VBTreeView();
        var (serialized, _) = new VbFrmFormatDeserializer().Deserialize(
            "Begin MSComctlLib.TreeView TreeView1\r\n" + string.Join("\r\n", instance.UnknownRawPropertyLines) + "\r\nEnd\r\n");
        // A missing Style loads 0; the new control's default is 7 (measured).
        if (instance.GetPropertyOrDefault(InBoxComponentClass.LoadedTypeNameProperty) is not null)
            tree.TrySetProperty("Style", new Vb6Value(0));
        tree.LoadEdge(
            serialized.Properties.TryGetValue("Appearance", out var appearance) ? Convert.ToInt32(appearance, System.Globalization.CultureInfo.InvariantCulture) : 1,
            serialized.Properties.TryGetValue("BorderStyle", out var border) ? Convert.ToInt32(border, System.Globalization.CultureInfo.InvariantCulture) : 0);
        foreach (var (name, raw) in serialized.Properties)
        {
            switch (name.ToLowerInvariant())
            {
                case "style" or "linestyle" or "labeledit":
                    tree.TrySetProperty(name, new Vb6Value(Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture)));
                    break;
                case "indentation":
                    tree.LoadHimetricIndentation(Convert.ToDouble(raw, System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case "hideselection" or "checkboxes" or "sorted" or "singlesel":
                    tree.TrySetProperty(name, new Vb6Value(Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture) != 0));
                    break;
                case "pathseparator": tree.TrySetProperty(name, new Vb6Value((string)raw)); break;
            }
        }
        return tree;
    }
}
