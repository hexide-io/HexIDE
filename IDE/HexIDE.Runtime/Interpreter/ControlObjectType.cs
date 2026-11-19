using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Interpreter;

// Runtime identities for measured control classes (#1563, #2265). The 5.0 controls still
// refused at F5 are not instantiated here; ProgressBar carries the library it was loaded under.
internal static class ControlObjectType
{
    // Scalar intrinsic control declarations checked by Set (#1563); arrays and UserControls are not measured here.
    private static readonly HashSet<string> IntrinsicClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CheckBox", "ComboBox", "CommandButton", "Data", "DirListBox", "DriveListBox", "FileListBox", "Frame",
        "HScrollBar", "Image", "Label", "Line", "ListBox", "Menu", "OLE", "OptionButton", "PictureBox", "Shape",
        "TextBox", "Timer", "VScrollBar",
    };

    internal static bool IsIntrinsic(string name) => IntrinsicClasses.Contains(name);

    internal static string? Of(object? value)
    {
        if (value is VBProgressBar bar) return bar.Library.TypeName("ProgressBar");
        var other = value switch
        {
            VBDTPicker => "MSComCtl2.DTPicker",
            VBMonthView => "MSComCtl2.MonthView",
            VBUpDown => "MSComCtl2.UpDown",
            VBAnimation => "MSComCtl2.Animation",
            VBFlatScrollBar => "MSComCtl2.FlatScrollBar",
            VBSSTab => "TabDlg.SSTab",
            VBMaskEdBox => "MSMask.MaskEdBox",
            VBRichTextBox => "RichTextLib.RichTextBox",
            VBCommonDialog => "MSComDlg.CommonDialog",
            _ => null,
        };
        if (other is not null) return other;
        var name = value switch
        {
            VBImageCombo => "ImageCombo",
            ImageComboItems => "ComboItems",
            ImageComboItem => "ComboItem",
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
