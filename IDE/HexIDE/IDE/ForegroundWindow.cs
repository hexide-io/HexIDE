using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.IDE;

/// <summary>
/// Which top-level window is the user actually looking at?
///
/// "The first visible window that isn't the main one" is the obvious answer and it is wrong as soon as
/// two are open at once. A running VB6 program puts a <c>VBFormRuntime</c> window on screen; a
/// <c>MsgBox</c> from that program puts a second window on top of it. Both are visible, both are
/// non-main, and the form was created first — so first-wins reports the form and the dialog on top of it
/// is invisible to anything asking this question.
///
/// Ownership answers it properly. Every dialog here is shown with <c>ShowDialog(owner)</c>, which sets
/// <see cref="Window.Owner"/>, so a window that owns another visible window is by definition underneath
/// it. Discard those and the foreground window is what remains.
/// </summary>
public static class ForegroundWindow
{
    /// <summary>
    /// The window a caller asked for: by default a visible window other than the IDE's own (a dialog first),
    /// falling back to the IDE; the IDE's own window on request; or one named by its title or a running form's name.
    /// </summary>
    /// <remarks>
    /// <para>Exists because the default is the wrong answer for a whole class of question. While a VB6
    /// program runs — <b>including while it is paused at a breakpoint</b> — <c>"auto"</c> usually picks the
    /// program's form, so every tool that resolves a path against it is addressing the program rather than
    /// the IDE, and the IDE's own editor becomes unreachable at exactly the moment its debugger state is
    /// worth looking at. That is gap 4 in <c>docs/mcp-server-gaps.md</c>, and it costs more than pixels:
    /// an Auto Data Tip carries readable text, and no path can be aimed at the editor to trigger one.</para>
    ///
    /// <para><b>Activation is not the lever.</b> Three separate attempts to move the foreground —
    /// <c>set_window_state</c>, breaking before the form is shown, and <c>activate_document_tab</c> — all
    /// succeed and change nothing here, because the preference is in target <i>selection</i>. So the choice
    /// has to be passed in, not arranged for.</para>
    ///
    /// <para><b>A name, not a third value.</b> A <c>"form"</c> scope was considered and left out: <c>"auto"</c>
    /// already usually picks a running form when one is up, so it would name a case that is already
    /// covered — and it would suggest an ability to pick <i>which</i> form, which an enum cannot do once a
    /// project shows more than one. A program of several forms does (#822), so a name picks one: a window's title,
    /// or a running form's name.</para>
    /// </remarks>
    /// <param name="scope">
    /// <c>"auto"</c> or null prefers any other visible window to the IDE, whether or not it is on top:
    /// a dialog first, then the one with focus, then the one opened last. <c>"ide"</c> (or <c>"main"</c>)
    /// picks the IDE's own window; a window's title or a running form's name picks that window. A program
    /// of several forms (#822) needs a name to reach a form the default does not pick.
    /// </param>
    /// <param name="ideByDefault">
    /// Whether the caller's own default is the IDE's window rather than <c>"auto"</c>, as get_window_state's and
    /// set_window_state's is (#2357). It changes only how an unknown name's refusal describes the choices.
    /// </param>
    public static (Window? Window, string? Error) Pick(
        string? scope, Window mainWindow, IReadOnlyList<Window> windows, bool ideByDefault = false)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return (Pick(mainWindow, windows), null);

        if (scope.Equals("ide", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("main", StringComparison.OrdinalIgnoreCase))
            return (mainWindow, null);

        // A window by its title, or a running form by its name, which outlives a caption the program changes.
        var named = windows.Where(w => w != mainWindow
            && (string.Equals(w.Title, scope, StringComparison.OrdinalIgnoreCase)
                || string.Equals(VBProps.GetName(w), scope, StringComparison.OrdinalIgnoreCase))).ToList();
        var shown = named.Where(w => w.IsVisible).ToList();
        if (shown.Count == 1)
            return (shown[0], null);
        if (shown.Count > 1)
            return (null, $"{shown.Count} open windows answer to '{scope}', as two instances of one form do: "
                          + $"{Titles(shown)}. Name one by a title only it has.");
        if (named.Count > 0)
            return (null, $"'{scope}' is hidden, as a form after Hide is, and a hidden window can't be reached: a "
                          + "person can't click it either.");

        // Named rather than silently treated as "auto". A caller who mistypes this is trying to reach a
        // window the default would not have given them, so falling back would answer a different question
        // than the one asked and look like it worked.
        var open = windows.Where(w => w != mainWindow && w.IsVisible).ToList();
        return (null, $"Unknown window '{scope}'. Use "
                    + (ideByDefault
                        ? "\"ide\" (the default here: the IDE window, even while a program is running), \"auto\" (any visible "
                          + "window other than the IDE's own, a dialog first), "
                        : "\"auto\" (the default: any visible window other than the IDE's own, a dialog first), \"ide\" (the "
                          + "IDE window, even while a program is running), ")
                    + "or the name or title of an open "
                    + (open.Count == 0
                        ? "window, and none is open beside the IDE."
                        : $"window: {Titles(open)}. A form's name is the stable handle: its caption can be empty, or change "
                          + "as the program runs."));
    }

    // Each window as a caller can name it: a running form by its name, then its title in quotes, and any other window
    // by its title alone.
    private static string Titles(IEnumerable<Window> windows) =>
        string.Join(", ", windows.Select(w => VBProps.GetName(w) is { Length: > 0 } name
            ? $"{name} (\"{w.Title}\")"
            : $"\"{w.Title}\""));

    /// <summary>
    /// Prefers a visible window other than <paramref name="mainWindow"/>: a dialog first, then the one
    /// with focus, then the one opened last, excluding windows that own another visible window. Falls
    /// back to <paramref name="mainWindow"/> when no candidate remains; does not read z-order.
    /// </summary>
    /// <param name="mainWindow">The application's main window; returned when no other candidate exists.</param>
    /// <param name="windows">All top-level windows, in creation order (as the lifetime reports them).</param>
    public static Window Pick(Window mainWindow, IReadOnlyList<Window> windows)
    {
        // A window that owns a visible window has that window sitting on top of it.
        var covered = new HashSet<Window>();
        foreach (var w in windows)
        {
            if (w.IsVisible && w.Owner is Window owner)
                covered.Add(owner);
        }

        // Best candidate wins, scored rather than ordered, because the signals disagree in practice:
        //   owned + active  a focused dialog — unambiguous
        //   owned           a dialog that never took focus, which is still what is on top
        //   active          a plain window with focus
        // IsActive alone is not enough: it is false for every window when the app is in the background,
        // and a headless test never sets it at all. Ownership still holds in both cases, which is why it
        // outranks focus here.
        Window? best = null;
        var bestScore = -1;
        foreach (var w in windows)
        {
            if (w == mainWindow || !w.IsVisible || covered.Contains(w))
                continue;

            var score = (w.Owner is Window ? 2 : 0) + (w.IsActive ? 1 : 0);
            // >= so that, all else equal, the most recently opened window wins: the lifetime appends,
            // so later in this list means later on screen.
            if (score >= bestScore)
            {
                bestScore = score;
                best = w;
            }
        }

        return best ?? mainWindow;
    }

    /// <summary>
    /// <paramref name="path"/> resolved in <paramref name="picked"/>, as the automation tools resolve one, with a failure naming
    /// that window when the choice was left to <c>"auto"</c> and fell on a window other than the IDE's own (#1264).
    /// </summary>
    /// <remarks>
    /// Any other window outranks the IDE's under <c>"auto"</c>, whether or not it is on top, the modeless Find window too.
    /// A path from an earlier dump of the IDE then failed with <c>no child matching 'Custom[MainView]' under 'Window'</c>,
    /// which names no window, so a caller hunted for a wrong path rather than a wrong window. Window <c>"ide"</c> is
    /// offered only for a path that resolves there: a typo, or a path into a running form while Find was picked, would
    /// be sent the wrong way.
    /// </remarks>
    /// <param name="pickedName">How <paramref name="picked"/> is named to a caller: its title, or what it shows.</param>
    public static (Control? Control, string? Error) Resolve(
        string path, string? scope, Window picked, Window mainWindow, string pickedName)
    {
        var (control, error) = HexIDE.Automation.UiAutomationDriver.Resolve(picked, path);
        if (control is not null || error is null || picked == mainWindow
            || !(string.IsNullOrWhiteSpace(scope) || scope.Equals("auto", StringComparison.OrdinalIgnoreCase)))
            return (control, error);
        // A path that is not one fails alike in every window, before any is looked in, so which window it was is no part
        // of why. These are the two such refusals Resolve makes.
        if (error.StartsWith(HexIDE.Automation.UiAutomationDriver.EmptyPath, StringComparison.Ordinal)
            || error.StartsWith(HexIDE.Automation.UiAutomationDriver.NotFromWindow, StringComparison.Ordinal))
            return (null, error);
        // Probed against the IDE's own only on failure, and only read: it says whether "ide" would have reached it.
        var inMain = HexIDE.Automation.UiAutomationDriver.Resolve(mainWindow, path).control is not null;
        return (null, $"{error}. The path was looked for in '{pickedName}', the window \"auto\" picked over the IDE's own"
                      + (inMain ? "; it resolves in the IDE's own window, which window \"ide\" reaches" : ""));
    }
}
