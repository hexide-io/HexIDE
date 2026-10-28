using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// A control's TabIndex and TabStop as a program reads and writes them (#1974). Measured on vb6.exe, native and p-code
/// agreeing (the oracle's "TabIndex and TabStop at run time"):
/// <list type="bullet">
/// <item>TabIndex reads as an Integer. Setting it moves the control to that place in one order across the whole form,
/// a control inside a Frame and an element of a control array included, and the controls between its old place and its
/// new one move up or down by one. A place past the last is the last.</item>
/// <item>It is converted as an Integer, so 1.5 and 2.5 are both 2 and 40000 is Overflow (6). Below 0 is 380, True
/// included, Null is 94, "abc" is 13, and Empty is 0. Nothing moves when it raises.</item>
/// <item>TabStop is a Boolean, converted as CBool converts it: "abc" is 13 and Null is 94. It leaves the order alone. An
/// option button's reads the stop its group allows it: False for an option beside a selected one, and True once it is
/// selected, whatever it was set to before.</item>
/// <item>A Timer has no TabIndex, and a Label or a Frame no TabStop: 438 late-bound.</item>
/// </list>
/// </summary>
internal static class TabOrder
{
    /// <summary>Whether the control has a TabIndex: every control VB6 gives one that HexIDE runs.</summary>
    public static bool HasTabIndex(Control c) =>
        UserControlMembers.HasTabIndex(c) || c is VBCheckBox or VBComboBox or VBCommandButton or VBFrame or VBScrollBar or VBLabel or VBListBox or VBOptionButton
            or VBPictureBox or VBTextBox or VBProgressBar or VBStatusBar or VBSSTab or VBUpDown;

    /// <summary>Whether the control has a TabStop: those with a TabIndex but the Label, the Frame and the ProgressBar.</summary>
    public static bool HasTabStop(Control c) => (UserControlMembers.IsUserControl(c) ? UserControlMembers.HasTabStop(c) : HasTabIndex(c)) && c is not (VBLabel or VBFrame or VBProgressBar or VBStatusBar);

    public static int Get(Control c) => KeyboardNavigation.GetTabIndex(c);

    /// <summary>Move <paramref name="control"/> to <paramref name="place"/> in its form's order.</summary>
    public static void Set(Control control, int place)
    {
        if (place < 0)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (!FormControls.TryOf(control, out var form))
        {
            KeyboardNavigation.SetTabIndex(control, place);
            return;
        }
        // Ordered as they are now, a tie in file order; then numbered from 0, which also closes any gap a file left.
        var order = form.All.Where(HasTabIndex).OrderBy(c => KeyboardNavigation.GetTabIndex(c)).ToList();
        order.Remove(control);
        order.Insert(System.Math.Min(place, order.Count), control);
        for (var i = 0; i < order.Count; i++)
            KeyboardNavigation.SetTabIndex(order[i], i);
    }

    /// <summary>The TabStop a program reads, which is the control's stop: for an option button, the one its group allows.</summary>
    public static bool GetTabStop(Control c) => c is VBOptionButton option ? option.GroupTabStop : KeyboardNavigation.GetIsTabStop(c);

    public static void SetTabStop(Control c, bool tabStop)
    {
        if (c is VBOptionButton option)
            option.SetTabStopFromCode(tabStop);
        else
            KeyboardNavigation.SetIsTabStop(c, tabStop);
    }
}
