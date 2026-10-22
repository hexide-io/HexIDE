using System.Linq;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A menu's members as a program reads and writes them (#1126). Measured on vb6.exe, native and p-code agreeing (the
/// oracle's "Menus at run time, and the menu bar"):
/// <list type="bullet">
/// <item>Caption reads as a String with its ampersand, and Checked, Enabled and Visible as Booleans. Checked takes any
/// number, as True when it is not 0.</item>
/// <item>Checked on a menu with sub-items, or on a separator, is 387, "'Checked' property can't be set on this control".
/// A separator's Enabled is 387 as well, and reads True.</item>
/// <item>A Caption of "-" on an item is 395, "Cannot use separator bar as menu name for this control". A separator takes
/// a new caption and becomes an item, which HexIDE refuses as not implemented.</item>
/// <item>Hiding the last visible menu under a menu is 387, "'Visible' property can't be set on this control", whether
/// that last one is an item or a separator. A top-level menu can be hidden.</item>
/// <item>Left is not a member: vb6.exe refuses <c>mnuNew.Left</c> when it compiles. HexIDE refuses a member a menu
/// doesn't have when the statement runs. Tag is a member, as on any control, and WindowList reads False.</item>
/// </list>
/// </summary>
internal static class MenuMembers
{
    private static VBRunTimeException CantSet(string property) =>
        new(new VBStandardError(387, $"'{property}' property can't be set on this control"));

    private static readonly VBStandardError SeparatorAsName = new(395, "Cannot use separator bar as menu name for this control");

    public static bool IsMenu(Control control) => control is VBMenu or VBMenuSeparator;

    /// <summary>
    /// A menu's own member, read: true with its value, false for a member a menu does not have, null for one the shared
    /// bindings read (Enabled, Visible and Tag).
    /// </summary>
    public static bool? TryGet(Control menu, PropertyClass property, out Vb6Value value)
    {
        value = default;
        if (ReferenceEquals(property, VBProperties.CaptionProperty))
            value = new Vb6Value(menu is VBMenu item ? item.Header as string ?? "" : "-");
        else if (ReferenceEquals(property, VBProperties.CheckedProperty))
            value = new Vb6Value(menu is VBMenu { Checked: true });
        else if (ReferenceEquals(property, VBProperties.EnabledProperty) && menu is VBMenuSeparator)
            value = new Vb6Value(true);
        else if (ReferenceEquals(property, VBProperties.WindowListProperty))
            value = new Vb6Value(false);
        else if (IsShared(property))
            return null;
        else
            return false;
        return true;
    }

    /// <summary>A menu's own member, written: as <see cref="TryGet"/>, raising VB6's error for a value it refuses.</summary>
    public static bool? TrySet(Control menu, PropertyClass property, Vb6Value value)
    {
        if (ReferenceEquals(property, VBProperties.CaptionProperty))
        {
            var caption = (string)AvaloniaInteroperability.ConvertForAssignment(property, value)!;
            if (menu is VBMenu item)
            {
                if (caption == "-")
                    throw new VBRunTimeException(SeparatorAsName);
                item.Header = caption;
            }
            else if (caption != "-")
                throw new System.NotImplementedException("Giving a menu separator a caption, which makes it an item, is not implemented (#1126)");
            return true;
        }
        if (ReferenceEquals(property, VBProperties.CheckedProperty))
        {
            var isChecked = (bool)AvaloniaInteroperability.ConvertForAssignment(property, value)!;
            if (menu is not VBMenu item || item.ItemCount > 0)
                throw CantSet("Checked");
            item.Checked = isChecked;
            return true;
        }
        if (ReferenceEquals(property, VBProperties.EnabledProperty) && menu is VBMenuSeparator)
            throw CantSet("Enabled");
        if (ReferenceEquals(property, VBProperties.VisibleProperty))
        {
            var visible = (bool)AvaloniaInteroperability.ConvertForAssignment(property, value)!;
            if (!visible && menu.Parent is MenuItem owner && !AnyOtherVisible(owner, menu))
                throw CantSet("Visible");
            if (menu.Parent is Menu bar)
                RefuseBarChange(bar, bar.IsVisible && !visible && !AnyOtherVisible(bar, menu) || !bar.IsVisible && visible);
        }
        if (ReferenceEquals(property, VBProperties.WindowListProperty))
            throw new System.NotImplementedException("Setting a menu's WindowList at run time is not implemented (#1126)");
        if (IsShared(property))
            return null;
        return false;
    }

    private static bool AnyOtherVisible(ItemsControl parent, Control menu) =>
        parent.Items.OfType<Control>().Any(other => !ReferenceEquals(other, menu) && other.IsVisible);

    /// <summary>
    /// Refuses a change that would make the menu bar appear or go away: the last visible top-level menu hidden, or one shown
    /// on a form whose top-level menus are all hidden, as a form that only pops its menus up has them. The bar's height is
    /// taken off the client area, so the window or ScaleHeight would have to change with it, and how is unmeasured.
    /// Leaving the bar in place would be a wrong ScaleHeight, so the run stops instead (#1140).
    /// </summary>
    internal static void RefuseBarChange(Menu bar, bool changes)
    {
        if (changes)
            throw new System.NotImplementedException(
                "Showing or hiding the menu bar at run time, by the last visible top-level menu's Visible, is not implemented (#1140)");
    }

    private static bool IsShared(PropertyClass property) =>
        ReferenceEquals(property, VBProperties.EnabledProperty) || ReferenceEquals(property, VBProperties.VisibleProperty)
                                                                || ReferenceEquals(property, VBProperties.TagProperty);
}
