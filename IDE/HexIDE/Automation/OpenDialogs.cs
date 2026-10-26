using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.VisualTree;

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

    // The watch of the call under way, for the wait for a program to record the dialog it named.
    private static readonly AsyncLocal<DialogWatch?> Current = new();

    /// <summary>
    /// Read on the UI thread before a call: the dialogs open, and <paramref name="target"/>, the window the call acts in
    /// (the IDE's, unless it addresses a window itself).
    /// </summary>
    public static DialogWatch Watch(IEnumerable<Window> windows, Window? target) => new(Among(windows), target);

    /// <summary>
    /// Makes <paramref name="watch"/> the one the rest of this async flow reports to: a wait for a program (#1232) that
    /// names a dialog in its reply records it here, through <see cref="NamedInReply"/>, so the note does not name it again.
    /// So does typing that stops for a dialog (#1257).
    /// </summary>
    public static void During(DialogWatch watch) => Current.Value = watch;

    /// <summary>Records that the reply already names <paramref name="dialog"/>, as the wait for a program and typing do.</summary>
    internal static void NamedInReply(Window dialog) => Current.Value?.Named.Add(dialog);

    /// <summary>
    /// What the reply should say of the modal dialogs open once the call is done, read on the UI thread, or null when there
    /// are none to name. Not named: the window the call acted in, a dialog under it, and one the wait for a program named.
    /// A call that did not act still names an unreported dialog, without claiming it reached behind it.
    /// Exactly that one: the wait names a dialog only while the program is busy, and names one at most, so a dialog over an
    /// idle program, as a compile error reached from a click leaves, or one beside another, was named by neither (#1253).
    /// </summary>
    public static string? Note(DialogWatch watch, IEnumerable<Window> windows, bool acted = true)
    {
        var known = new HashSet<Window>();
        for (var at = watch.Target; at is not null; at = at.Owner as Window)
            known.Add(at);
        var named = Among(windows)
            .Where(d => !known.Contains(d) && !watch.Named.Contains(d)).ToList();
        if (named.Count == 0)
            return null;

        var parts = named.Select(d => $"{DescriptionOf(d)}, a modal dialog over {OwnerOf(d)}, "
            + (!watch.Before.Contains(d) ? "opened during this call and waits for an answer"
                : Blocks(d, watch.Target) ? "is still open and waits for an answer"
                    + (acted ? "; this call reached past it, as a person could not" : "")
                : "is open and waits for an answer"));
        return string.Join("; ", parts) + "; dump_visual_tree with 'window' set to its title shows it, and interact answers it.";
    }

    // Read the same live controls and automation names a tree dump exposes, including disabled choices. Every text that
    // names a waiting dialog goes through this, since its title alone does not tell the reset prompt from the run-time
    // error dialog: both are 'HexIDE' (#1365, #1975).
    internal static string DescriptionOf(Window dialog, string? title = null)
    {
        var controls = dialog.GetVisualDescendants().OfType<Control>().ToList();
        var content = dialog.Content;
        var kind = content switch
        {
            HexIDE.Runtime.Dialogs.MessageBox => "a message box",
            HexIDE.Forms.Views.RuntimeErrorView => "the run-time error dialog",
            Control { DataContext: HexIDE.Forms.ViewModels.RuntimeErrorViewModel } => "the run-time error dialog",
            HexIDE.Forms.ViewModels.RuntimeErrorViewModel => "the run-time error dialog",
            _ => null,
        };
        var buttons = string.Join("/", controls.OfType<Button>()
            .Where(b => b.IsEffectivelyVisible)
            .Select(b => ControlAutomationPeer.CreatePeerForElement(b)?.GetName()?.Replace("_", ""))
            .Where(name => !string.IsNullOrWhiteSpace(name)));
        var details = new List<string>();
        if (kind is not null)
        {
            // Restrict text to the content view: template chrome and button labels are not the message.
            var view = content as Control ?? controls.OfType<HexIDE.Forms.Views.RuntimeErrorView>().FirstOrDefault();
            var text = view?.GetVisualDescendants().OfType<TextBlock>()
                .LastOrDefault(t => t.IsEffectivelyVisible && !t.GetVisualAncestors().OfType<Button>().Any()
                    && !string.IsNullOrWhiteSpace(t.Text))?.Text;
            details.Add(kind + (text is null ? "" : $": \"{FirstLine(text)}\""));
        }
        if (buttons.Length > 0)
            details.Add(buttons);
        return $"'{title ?? dialog.Title}'" + (details.Count == 0 ? "" : $" ({string.Join(", ", details)})");
    }

    private static string FirstLine(string text)
    {
        const int limit = 100;
        var lineEnd = text.IndexOfAny(['\r', '\n', '\u0085', '\u2028', '\u2029']);
        var line = (lineEnd < 0 ? text : text[..lineEnd]).Trim();
        if (line.Length > limit || (lineEnd >= 0 && line.Length >= limit))
            return line[..(limit - 1)].TrimEnd() + "…";
        return line + (lineEnd >= 0 ? "…" : "");
    }

    // A dialog disables its owner, which, if a dialog itself, has disabled its own.
    internal static bool Blocks(Window dialog, Window? target)
    {
        for (var at = dialog.Owner as Window; at is not null; at = at.Owner as Window)
            if (at == target)
                return true;
        return false;
    }

    internal static string OwnerOf(Window dialog) => dialog.Owner switch
    {
        HexIDE.Runtime.IModuleExecutionRoot and Window form => $"the running form '{form.Title}'",
        Window owner => $"'{owner.Title}'",
        _ => "no window",
    };
}

/// <summary>The dialogs open before a call, and where it acts: see <see cref="OpenDialogs.Watch"/>.</summary>
public sealed class DialogWatch
{
    internal DialogWatch(List<Window> before, Window? target)
    {
        Before = before;
        Target = target;
    }

    internal List<Window> Before { get; }
    internal Window? Target { get; }
    // Written by the wait off the UI thread and read by the note on it, one after the other.
    internal HashSet<Window> Named { get; } = [];
}
