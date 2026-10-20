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
    /// <summary>
    /// The modal dialogs among <paramref name="windows"/>: those shown with ShowDialog, and a program's MsgBox or InputBox,
    /// which waits for an answer in a window of its own when the form it was given is hidden.
    /// </summary>
    public static List<Window> Among(IEnumerable<Window> windows) =>
        [.. windows.Where(w => w.IsVisible
            && (w.IsDialog || w.Content is HexIDE.Runtime.Dialogs.MessageBox or HexIDE.Runtime.Dialogs.InputBox))];

    /// <summary>
    /// Read on the UI thread before a call: the dialogs open, and <paramref name="target"/>, the window the call acts in
    /// (the IDE's, unless it addresses a window itself). <paramref name="waitsForProgram"/> is true for a tool that waits
    /// for the program behind its window (#1232); when that window is a program's, its dialogs are left to that wait
    /// while the program is held, as the wait names them then.
    /// </summary>
    public static DialogWatch Watch(IEnumerable<Window> windows, Window? target, bool waitsForProgram)
    {
        var program = waitsForProgram && target is not null ? UiAutomationDriver.ProgramOf(target)?.Pump : null;
        return new DialogWatch(Among(windows), target, program);
    }

    /// <summary>
    /// What the reply should say of the modal dialogs open once the call is done, read on the UI thread, or null when there
    /// are none to name. Not named: the window the call acted in, a dialog under it, and the dialog of a program still held,
    /// which the wait for the program names. An idle program's is named here: the wait returns at idle naming only forms, and
    /// a compile error reached from a click shows its dialog without holding the program, so a clean reply hid it (#1253).
    /// </summary>
    public static string? Note(DialogWatch watch, IEnumerable<Window> windows)
    {
        var known = new HashSet<Window>();
        for (var at = watch.Target; at is not null; at = at.Owner as Window)
            known.Add(at);
        var named = Among(windows)
            .Where(d => !known.Contains(d)
                        && !(watch.Program is { IsIdle: false } pump && UiAutomationDriver.ProgramOf(d)?.Pump == pump))
            .ToList();
        if (named.Count == 0)
            return null;

        var parts = named.Select(d => $"'{d.Title}', a modal dialog over {OwnerOf(d)}, "
            + (!watch.Before.Contains(d) ? "opened during this call and waits for an answer."
                : Blocks(d, watch.Target) ? "is still open and waits for an answer; this call reached past it, as a person could not."
                : "is open and waits for an answer."));
        return string.Join(" ", parts) + " dump_visual_tree with 'window' set to its title shows it, and interact answers it.";
    }

    // A dialog disables its owner, which, if a dialog itself, has disabled its own.
    private static bool Blocks(Window dialog, Window? target)
    {
        for (var at = dialog.Owner as Window; at is not null; at = at.Owner as Window)
            if (at == target)
                return true;
        return false;
    }

    private static string OwnerOf(Window dialog) => dialog.Owner switch
    {
        HexIDE.Runtime.IModuleExecutionRoot and Window form => $"the running form '{form.Title}'",
        Window owner => $"'{owner.Title}'",
        _ => "no window",
    };
}

/// <summary>The dialogs open before a call, and where it acts: see <see cref="OpenDialogs.Watch"/>.</summary>
public sealed class DialogWatch
{
    internal DialogWatch(List<Window> before, Window? target, HexIDE.Runtime.EventPump? program)
    {
        Before = before;
        Target = target;
        Program = program;
    }

    internal List<Window> Before { get; }
    internal Window? Target { get; }
    internal HexIDE.Runtime.EventPump? Program { get; }
}
