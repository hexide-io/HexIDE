using System;
using System.Collections.Generic;
using Avalonia.Controls;

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
    /// The window a caller asked for: the frontmost one by default, or the IDE's own window on request.
    /// </summary>
    /// <remarks>
    /// <para>Exists because "frontmost" is the wrong answer for a whole class of question. While a VB6
    /// program runs — <b>including while it is paused at a breakpoint</b> — the frontmost window is the
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
    /// <para><b>Two values, not three.</b> A <c>"form"</c> scope was considered and left out: <c>"auto"</c>
    /// already resolves to the running form whenever one is up, so it would name a case that is already
    /// covered — and it would suggest an ability to pick <i>which</i> form, which an enum cannot do once a
    /// project shows more than one.</para>
    /// </remarks>
    /// <param name="scope">
    /// <c>"auto"</c> or null for the frontmost window; <c>"ide"</c> (or <c>"main"</c>) for the IDE's own
    /// window, whatever is in front of it.
    /// </param>
    public static (Window? Window, string? Error) Pick(
        string? scope, Window mainWindow, IReadOnlyList<Window> windows)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return (Pick(mainWindow, windows), null);

        if (scope.Equals("ide", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("main", StringComparison.OrdinalIgnoreCase))
            return (mainWindow, null);

        // Named rather than silently treated as "auto". A caller who mistypes this is trying to reach a
        // window the default would not have given them, so falling back would answer a different question
        // than the one asked and look like it worked.
        return (null, $"Unknown window '{scope}'. Use \"auto\" (the frontmost window, the default) "
                    + "or \"ide\" (the IDE window, even while a program is running).");
    }

    /// <summary>
    /// Picks the frontmost visible window, falling back to <paramref name="mainWindow"/> when nothing
    /// else is on screen.
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
}
