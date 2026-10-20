using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace HexIDE.Automation;

/// <summary>
/// The modal dialogs open over the IDE or a running program, for a tool's reply to name (#681). A modal blocks a
/// person's input to the windows behind it, but a tool acts on them all the same, so a caller that only acts, and never
/// looks, could build a sequence of edits and runs under a dialog nobody answered: a refusal's message box after Set
/// Next Statement, the run-time error dialog a program is held at, Add Procedure left open.
/// </summary>
public static class OpenDialogs
{
    /// <summary>The modal dialogs among <paramref name="windows"/>: those shown with ShowDialog and still on screen.</summary>
    public static List<Window> Among(IEnumerable<Window> windows) => [.. windows.Where(w => w.IsVisible && w.IsDialog)];

    /// <summary>
    /// What a reply should say of the modal dialogs open once its call is done, or null when there are none to name.
    /// One the call opened is always named. One open before it is named when <paramref name="namesOneAlreadyOpen"/>,
    /// which a tool that addresses a window itself turns off: it acts in the dialog, or reports which window it used.
    /// A program's own dialog, a MsgBox over its form, is left to those tools, whose wait for the program names it.
    /// </summary>
    public static string? Note(IReadOnlyCollection<Window> before, IReadOnlyList<Window> after, bool namesOneAlreadyOpen,
        bool leavesProgramDialogs = false)
    {
        var named = after.Where(w => !(leavesProgramDialogs && OwnedByProgram(w))).ToList();
        var opened = named.Where(w => !before.Contains(w)).ToList();
        var stillOpen = namesOneAlreadyOpen ? named.Where(before.Contains).ToList() : [];
        if (opened.Count == 0 && stillOpen.Count == 0)
            return null;

        var parts = new List<string>();
        if (opened.Count > 0)
            parts.Add($"This call opened {Describe(opened)}.");
        if (stillOpen.Count > 0)
            parts.Add($"{Capitalised(Describe(stillOpen))} was open before this call and still is, so the call acted on "
                      + "what lies behind it, which a person could not reach.");
        parts.Add("Until it is answered a person can use nothing behind it: dump_visual_tree {} shows it, and interact "
                  + "answers it.");
        return string.Join(" ", parts);
    }

    private static string Describe(List<Window> dialogs) =>
        string.Join(" and ", dialogs.Select(d => $"a modal dialog, '{d.Title}', over {OwnerOf(d)}"));

    private static string OwnerOf(Window dialog) => dialog.Owner switch
    {
        HexIDE.Runtime.IModuleExecutionRoot and Window form => $"the running form '{form.Title}'",
        Window owner => $"'{owner.Title}'",
        _ => "no window",
    };

    // By its owner, not its content: the IDE's own message boxes are the same MessageBox control as a program's.
    private static bool OwnedByProgram(Window dialog)
    {
        for (var at = dialog.Owner as Window; at is not null; at = at.Owner as Window)
            if (at is HexIDE.Runtime.IModuleExecutionRoot)
                return true;
        return false;
    }

    private static string Capitalised(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
