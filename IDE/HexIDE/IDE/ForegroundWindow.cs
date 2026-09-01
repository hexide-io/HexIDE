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
