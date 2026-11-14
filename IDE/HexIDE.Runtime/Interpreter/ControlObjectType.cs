using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Interpreter;

// Runtime identities for the measured common-control classes (#2265). The 5.0 controls still
// refused at F5 are not instantiated here; ProgressBar carries the library it was loaded under.
internal static class ControlObjectType
{
    internal static string? Of(object? value)
    {
        if (value is VBProgressBar bar) return bar.Library.TypeName("ProgressBar");
        var name = value switch
        {
            VBImageList => "ImageList",
            ImageListImages => "ListImages",
            ImageListImage => "ListImage",
            VBListView => "ListView",
            ListViewItems => "ListItems",
            ListViewItem => "ListItem",
            ListViewColumns => "ColumnHeaders",
            ListViewColumn => "ColumnHeader",
            ListViewSubItems => "ListSubItems",
            ListViewSubItem => "ListSubItem",
            VBTreeView => "TreeView",
            TreeNodes => "Nodes",
            TreeNode => "Node",
            VBStatusBar => "StatusBar",
            StatusBarPanels => "Panels",
            StatusBarPanel => "Panel",
            VBToolbar => "Toolbar",
            ToolbarItems items => items.TypeName == "IButtons" ? "Buttons" : "ButtonMenus",
            ToolbarItem item => item.TypeName == "IButton" ? "Button" : "ButtonMenu",
            VBTabStrip => "TabStrip",
            TabStripTabs => "Tabs",
            TabStripTab => "Tab",
            _ => null,
        };
        return name is null ? null : ControlLibraries.MSComctlLib.TypeName(name);
    }

    internal static bool IsDeclared(string name) => ControlLibraries.ForTypeName(name) is { } library
        && library.ControlClasses.Any(c => name.Equals(library.TypeName(c), StringComparison.OrdinalIgnoreCase));
}
