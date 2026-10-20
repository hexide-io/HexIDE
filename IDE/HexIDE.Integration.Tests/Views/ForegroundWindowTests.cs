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

    // ── Choosing the window explicitly (gap 4) ───────────────────────────────────────

    [AvaloniaFact]
    public void Ide_scope_returns_the_main_window_even_while_a_form_is_running()
    {
        // The whole point of the scope. "auto" answers "what is the user looking at"; a tool verifying the
        // IDE's own paused-state editor is asking a different question, and used to get the program's form.
        var main = Shown("HexIDE");
        var form = Shown("Form1");

        ForegroundWindow.Pick("auto", main, new[] { main, form }).Window.Should().BeSameAs(form);
        ForegroundWindow.Pick("ide", main, new[] { main, form }).Window.Should().BeSameAs(main);
    }

    [AvaloniaFact]
    public void Ide_scope_wins_over_a_dialog_too()
    {
        // A dialog outranks a form under "auto" — it must not outrank an explicit request.
        var main = Shown("HexIDE");
        var form = Shown("Form1");
        var dialog = new Window { Title = "Run-time error" };
        _ = dialog.ShowDialog(form);

        ForegroundWindow.Pick("auto", main, new[] { main, form, dialog }).Window.Should().BeSameAs(dialog);
        ForegroundWindow.Pick("ide", main, new[] { main, form, dialog }).Window.Should().BeSameAs(main);
    }

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void An_absent_or_auto_scope_keeps_the_historical_behaviour(string? scope)
    {
        // Every existing caller passes nothing. If these diverged, adding the parameter would have changed
        // what six tools already do.
        var main = Shown("HexIDE");
        var form = Shown("Form1");

        ForegroundWindow.Pick(scope, main, new[] { main, form }).Window
            .Should().BeSameAs(ForegroundWindow.Pick(main, new[] { main, form }));
    }

    [AvaloniaFact]
    public void An_unknown_scope_is_named_rather_than_quietly_treated_as_auto()
    {
        // Falling back would answer a different question than the one asked and look like it worked: the
        // caller wanted a window the default does not give them, which is the only reason to pass this.
        var main = Shown("HexIDE");
        var form = Shown("Form1");

        var (window, error) = ForegroundWindow.Pick("editor", main, new[] { main, form });

        window.Should().BeNull();
        error.Should().Contain("editor").And.Contain("auto").And.Contain("ide");
    }

    // ── A window by its name (#822) ───────────────────────────────────────────────────────────────────────

    private static Window Form(string name, string title)
    {
        var w = Shown(title);
        HexIDE.Runtime.BuiltinControls.VBProps.SetName(w, name);
        return w;
    }

    [AvaloniaFact]
    public void A_form_behind_another_is_reached_by_its_title_or_its_name()
    {
        var main = Shown("HexIDE");
        var form1 = Form("Form1", "Main form");
        var form2 = Form("Form2", "Form2");

        ForegroundWindow.Pick(null, main, [main, form1, form2]).Window.Should().BeSameAs(form2, "it opened last");
        ForegroundWindow.Pick("Main form", main, [main, form1, form2]).Window.Should().BeSameAs(form1);
        ForegroundWindow.Pick("form1", main, [main, form1, form2]).Window.Should().BeSameAs(form1, "names ignore case");
    }

    [AvaloniaFact]
    public void An_unknown_name_is_refused_with_the_names_and_titles_of_the_open_windows()
    {
        var main = Shown("HexIDE");
        var form1 = Form("Form1", "Score: 0");
        var tool = Shown("A plain window");

        var (window, error) = ForegroundWindow.Pick("Form3", main, [main, form1, tool]);

        window.Should().BeNull();
        // The name first, because a caption can be empty or change; a window that isn't a form by its title alone.
        error.Should().Contain("Unknown window 'Form3'").And.Contain("Form1 (\"Score: 0\")")
            .And.Contain("\"A plain window\"").And.Contain("stable handle").And.NotContain("HexIDE");
    }

    [AvaloniaFact]
    public void Two_windows_of_one_name_are_refused_as_ambiguous_and_a_hidden_one_as_unreachable()
    {
        var main = Shown("HexIDE");
        var first = Form("Form1", "Form1");
        var second = Form("Form1", "Form1 again");

        ForegroundWindow.Pick("Form1", main, [main, first, second]).Error.Should().Contain("2 open windows answer to 'Form1'");

        second.Hide();
        ForegroundWindow.Pick("Form1 again", main, [main, first, second]).Error.Should().Contain("is hidden");
        ForegroundWindow.Pick("Form1", main, [main, first, second]).Window.Should().BeSameAs(first, "only one is showing");
    }

    [AvaloniaFact]
    public void A_modeless_ide_window_such_as_find_is_preferred_over_the_main_window()
    {
        // Find is shown with Show() and no owner, a running form's shape, so "auto" picks it as it picks a form (#1264).
        var main = Shown("HexIDE");
        var find = Shown("Find");

        ForegroundWindow.Pick(null, main, [main, find]).Window.Should().BeSameAs(find);
    }

    [AvaloniaFact]
    public void A_path_that_fails_in_a_window_auto_picked_over_the_ide_names_that_window()
    {
        // A path from an earlier dump of the IDE, looked for in Find, failed naming no window (#1264).
        var main = Shown("HexIDE");
        var find = Shown("Find");
        var (control, error) = HexIDE.Automation.UiAutomationDriver.Resolve(find, "Window/Custom[MainView]");
        control.Should().BeNull();

        ForegroundWindow.Unresolved(error!, null, find, main, "Find").Should().Be(
            $"{error}. The path was looked for in 'Find', the window in front, as window \"auto\" picks; window \"ide\" "
            + "reaches the IDE's own");
        ForegroundWindow.Unresolved(error!, "AUTO", find, main, "Find").Should().Contain("'Find', the window in front");
        ForegroundWindow.Unresolved(error!, " ", find, main, "Find").Should().Contain("'Find', the window in front");
    }

    [AvaloniaTheory]
    [InlineData("ide")]
    [InlineData("Find")]
    public void A_path_that_fails_in_a_window_asked_for_by_name_is_left_as_it_is(string scope)
    {
        var main = Shown("HexIDE");
        var find = Shown("Find");

        ForegroundWindow.Unresolved("no child", scope, find, main, "Find").Should().Be("no child");
        ForegroundWindow.Unresolved("no child", null, main, main, "HexIDE").Should().Be("no child");
    }
}
