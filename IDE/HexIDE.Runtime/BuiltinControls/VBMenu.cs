using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A VB6 menu item on a running form (#1126): what <c>mnuFile</c> names in code, and what <c>TypeName</c> calls a Menu.
/// It is drawn as Avalonia's own MenuItem. The one behaviour it changes is that choosing an item never ticks or unticks it:
/// in VB6 a program sets Checked itself, usually in the item's Click, and a menu that toggled on its own would undo that.
/// </summary>
public class VBMenu : MenuItem
{
    protected override Type StyleKeyOverride => typeof(MenuItem);

    protected override void OnClick(RoutedEventArgs e)
    {
        // MenuItem.OnClick flips IsChecked for a CheckBox toggle. Keep what the program set, and raise Click as before.
        var isChecked = IsChecked;
        base.OnClick(e);
        if (IsChecked != isChecked)
            SetCurrentValue(IsCheckedProperty, isChecked);
    }
}

/// <summary>
/// A VB6 menu whose caption is a hyphen: drawn as a rule, and a Menu to a program like any other (#1126).
/// </summary>
public class VBMenuSeparator : Separator
{
    protected override Type StyleKeyOverride => typeof(Separator);
}
