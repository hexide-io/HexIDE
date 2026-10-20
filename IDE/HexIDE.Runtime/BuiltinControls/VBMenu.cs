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
    private bool _checked;

    protected override Type StyleKeyOverride => typeof(MenuItem);

    /// <summary>
    /// The item's tick as the program, or the form file, last set it. Set this rather than <c>IsChecked</c>: a click puts
    /// <c>IsChecked</c> back to it.
    /// </summary>
    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            IsChecked = value;
        }
    }

    protected override void OnClick(RoutedEventArgs e)
    {
        // Avalonia's menu flips a CheckBox item's IsChecked before it raises Click, so by now the tick has already moved
        // (#947). Put back what the program set before its Click runs, so that a Click setting Checked has the last word.
        if (IsChecked != _checked)
            SetCurrentValue(IsCheckedProperty, _checked);
        base.OnClick(e);
    }
}

/// <summary>
/// A VB6 menu whose caption is a hyphen: drawn as a rule, and a Menu to a program like any other (#1126).
/// </summary>
public class VBMenuSeparator : Separator
{
    protected override Type StyleKeyOverride => typeof(Separator);
}
