using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Guards the window-picking rule the MCP automation tools use to decide what they are looking at.
///
/// The defect (#61): `take_snapshot` and `dump_visual_tree` took "the first visible non-main window",
/// which is correct only while exactly one is open. Run a VB6 program and a `VBFormRuntime` window
/// appears; have that program call `MsgBox` and a second window appears on top of it. First-wins
/// returned the form — created earlier — so the dialog sitting over it was invisible to automation,
/// and stayed invisible until the form was torn down. An agent driving the IDE could not see the
/// message box it had just triggered, nor a runtime error dialog.
///
/// Ownership is the signal that survives: `ShowDialog(owner)` sets <see cref="Window.Owner"/>, so a
/// window that owns a visible window is underneath it by construction. These tests pin that, and
/// deliberately do not rely on <c>IsActive</c> — headless never sets it, which is exactly the
/// robustness the rule needs.
/// </summary>
public class ForegroundWindowTests
{
    private static Window Shown(string title)
    {
        var w = new Window { Title = title };
        w.Show();
        return w;
    }

    [AvaloniaFact]
    public void With_nothing_else_open_the_main_window_is_the_foreground()
    {
        var main = Shown("HexIDE");

        ForegroundWindow.Pick(main, new[] { main }).Should().BeSameAs(main);
    }

    [AvaloniaFact]
    public void A_running_form_is_preferred_over_the_main_window()
    {
        var main = Shown("HexIDE");
        var form = Shown("Form1");

        ForegroundWindow.Pick(main, new[] { main, form }).Should().BeSameAs(form);
    }

    [AvaloniaFact]
    public void A_dialog_over_a_running_form_is_the_foreground_window()
    {
        // The regression itself. Three visible windows, and the one that matters was created last.
        var main = Shown("HexIDE");
        var form = Shown("Form1");
        var msgBox = new Window { Title = "MsgBox" };
        _ = msgBox.ShowDialog(form);

        ForegroundWindow.Pick(main, new[] { main, form, msgBox })
            .Should().BeSameAs(msgBox, "the dialog owns the foreground, not the form underneath it");
    }

    [AvaloniaFact]
    public void A_dialog_over_the_main_window_is_still_found()
    {
        var main = Shown("HexIDE");
        var options = new Window { Title = "Options" };
        _ = options.ShowDialog(main);

        ForegroundWindow.Pick(main, new[] { main, options }).Should().BeSameAs(options);
    }

    [AvaloniaFact]
    public void Closing_the_dialog_hands_the_foreground_back_to_the_form()
    {
        var main = Shown("HexIDE");
        var form = Shown("Form1");
        var msgBox = new Window { Title = "MsgBox" };
        _ = msgBox.ShowDialog(form);
        msgBox.Close();

        // A closed window stops being visible, so it stops covering its owner. Without this the tools
        // would stay pointed at a dead dialog for the rest of the session.
        ForegroundWindow.Pick(main, new[] { main, form, msgBox }).Should().BeSameAs(form);
    }

    [AvaloniaFact]
    public void An_invisible_window_is_never_chosen()
    {
        var main = Shown("HexIDE");
        var hidden = new Window { Title = "not shown" };

        ForegroundWindow.Pick(main, new[] { main, hidden }).Should().BeSameAs(main);
    }

    [AvaloniaFact]
    public void Among_unowned_windows_the_most_recently_opened_wins()
    {
        // Two tool windows with no ownership between them: the lifetime appends, so later in the list
        // is later on screen, and that is the best guess available.
        var main = Shown("HexIDE");
        var first = Shown("Form1");
        var second = Shown("Form2");

        ForegroundWindow.Pick(main, new[] { main, first, second }).Should().BeSameAs(second);
    }

    [AvaloniaFact]
    public void A_dialog_outranks_a_form_opened_after_it()
    {
        // Ownership beats recency: the modal is what the user must dismiss, even though another
        // window appeared afterwards.
        var main = Shown("HexIDE");
        var form = Shown("Form1");
        var msgBox = new Window { Title = "MsgBox" };
        _ = msgBox.ShowDialog(form);
        var later = Shown("Form2");

        ForegroundWindow.Pick(main, new[] { main, form, msgBox, later }).Should().BeSameAs(msgBox);
    }
}
