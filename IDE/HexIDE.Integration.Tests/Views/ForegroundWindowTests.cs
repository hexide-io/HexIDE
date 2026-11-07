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
public class ForegroundWindowTests : IDisposable
{
    private readonly List<Window> windows = new();

    public void Dispose()
    {
        // Owners are opened first, so close their dialogs first.
        foreach (var window in windows.AsEnumerable().Reverse())
            window.Close();
        windows.Clear();
    }

    private Window Track(Window window)
    {
        windows.Add(window);
        return window;
    }

    private Window Shown(string title)
    {
        var w = Track(new Window { Title = title });
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
        var msgBox = Track(new Window { Title = "MsgBox" });
        _ = msgBox.ShowDialog(form);

        ForegroundWindow.Pick(main, new[] { main, form, msgBox })
            .Should().BeSameAs(msgBox, "the dialog owns the foreground, not the form underneath it");
    }

    [AvaloniaFact]
    public void A_dialog_over_the_main_window_is_still_found()
    {
        var main = Shown("HexIDE");
        var options = Track(new Window { Title = "Options" });
        _ = options.ShowDialog(main);

        ForegroundWindow.Pick(main, new[] { main, options }).Should().BeSameAs(options);
    }

    [AvaloniaFact]
    public void Closing_the_dialog_hands_the_foreground_back_to_the_form()
    {
        var main = Shown("HexIDE");
        var form = Shown("Form1");
        var msgBox = Track(new Window { Title = "MsgBox" });
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
        var msgBox = Track(new Window { Title = "MsgBox" });
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
        var dialog = Track(new Window { Title = "Run-time error" });
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

    // get_window_state and set_window_state reach the IDE's own window when no window is named, as before they took one,
    // so an unknown name's refusal there must not call "auto" the default (#2357).
    [AvaloniaFact]
    public void An_unknown_scope_names_the_default_of_the_tool_that_asked()
    {
        var main = Shown("HexIDE");
        var form = Shown("Form1");

        ForegroundWindow.Pick("Nope", main, [main, form]).Error.Should()
            .Contain("\"auto\" (the default: ").And.NotContain("the default here");
        ForegroundWindow.Pick("Nope", main, [main, form], ideByDefault: true).Error.Should()
            .StartWith("Unknown window 'Nope'. Use \"ide\" (the default here: the IDE window, even while a program is running), "
                       + "\"auto\" (any visible window other than the IDE's own, a dialog first), or the name or title of an open")
            .And.NotContain("(the default: ");
    }

    [AvaloniaFact]
    public void An_unknown_scope_does_not_describe_auto_as_frontmost()
    {
        var main = Shown("HexIDE");
        var form = Shown("Form1");

        ForegroundWindow.Pick("Form3", main, [main, form]).Error.Should().NotContain("frontmost");
    }

    // ── A window by its name (#822) ───────────────────────────────────────────────────────────────────────

    private Window Form(string name, string title)
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

        // Side by side, no name tells them apart; the refusal says what does reach one, not to name a title only one has
        // (#2712).
        ForegroundWindow.Pick("Form1", main, [main, first, second]).Error.Should().Contain("2 open windows answer to 'Form1'")
            .And.Contain("\"auto\" reaches one of them, Form1 (\"Form1 again\"), the one opened last")
            .And.NotContain("a title only it has");

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


    private static IEnumerable<HexIDE.Automation.UiNode> Flatten(HexIDE.Automation.UiNode node) =>
        node.Children.SelectMany(Flatten).Prepend(node);

    // A path to the control named name, as a dump gives it.
    private static string PathTo(Window window, string name) =>
        Flatten(HexIDE.Automation.UiAutomationDriver.Dump(window, "Window", 10, interactiveOnly: false))
            .First(n => n.Path.EndsWith("[" + name + "]", StringComparison.Ordinal)).Path;

    // The IDE's window holds a button, Find a text box: a path into one is no path into the other.
    private (Window Main, Window Find) IdeWithFind()
    {
        var main = Track(new Window { Title = "HexIDE", Content = new Button { Name = "Go" } });
        main.Show();
        var find = Track(new Window { Title = "Find", Content = new TextBox { Name = "FindWhat" } });
        find.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return (main, find);
    }

    [AvaloniaFact]
    public void A_path_into_the_ide_that_fails_in_a_window_auto_picked_names_it_and_points_to_ide()
    {
        // A path from an earlier dump of the IDE, looked for in Find, failed naming no window (#1264).
        var (main, find) = IdeWithFind();
        var go = PathTo(main, "Go");
        HexIDE.Automation.UiAutomationDriver.Resolve(main, go).control.Should().NotBeNull();
        var (_, bare) = HexIDE.Automation.UiAutomationDriver.Resolve(find, go);

        var (control, error) = ForegroundWindow.Resolve(go, null, find, main, "Find");

        control.Should().BeNull();
        error.Should().Be($"{bare}. The path was looked for in 'Find', the window \"auto\" picked over the IDE's own; it "
                          + "resolves in the IDE's own window, which window \"ide\" reaches");
        ForegroundWindow.Resolve(go, "AUTO", find, main, "Find").Error.Should().Be(error);
        ForegroundWindow.Resolve(go, " ", find, main, "Find").Error.Should().Be(error);
    }

    [AvaloniaFact]
    public void A_path_that_resolves_nowhere_names_the_window_auto_picked_and_does_not_point_to_ide()
    {
        // A typo, or a path into a running form while Find was picked: "ide" would send the caller the wrong way.
        var (main, find) = IdeWithFind();
        var (_, bare) = HexIDE.Automation.UiAutomationDriver.Resolve(find, "Window/Button[Gone]");

        ForegroundWindow.Resolve("Window/Button[Gone]", null, find, main, "Find").Error.Should()
            .Be($"{bare}. The path was looked for in 'Find', the window \"auto\" picked over the IDE's own");
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("Find/x")]
    public void A_path_that_is_not_one_is_refused_without_naming_a_window(string path)
    {
        var (main, find) = IdeWithFind();
        var (_, bare) = HexIDE.Automation.UiAutomationDriver.Resolve(find, path);

        ForegroundWindow.Resolve(path, null, find, main, "Find").Error.Should().Be(bare);
    }

    [AvaloniaFact]
    public void A_path_that_fails_in_a_window_asked_for_by_name_or_in_the_ide_s_own_is_left_as_it_is()
    {
        var (main, find) = IdeWithFind();
        var (go, findWhat) = (PathTo(main, "Go"), PathTo(find, "FindWhat"));
        var (_, inFind) = HexIDE.Automation.UiAutomationDriver.Resolve(find, go);
        var (_, inMain) = HexIDE.Automation.UiAutomationDriver.Resolve(main, findWhat);

        ForegroundWindow.Resolve(go, "Find", find, main, "Find").Error.Should().Be(inFind);
        ForegroundWindow.Resolve(findWhat, null, main, main, "HexIDE").Error.Should().Be(inMain);
    }

    [AvaloniaFact]
    public void A_path_that_resolves_in_the_window_auto_picked_reaches_its_control()
    {
        var (main, find) = IdeWithFind();
        var findWhat = PathTo(find, "FindWhat");

        ForegroundWindow.Resolve(findWhat, null, find, main, "Find").Control.Should().BeOfType<TextBox>();
    }

    // The Find dialog's message box has the dialog's own title, so "Find" named both and was refused with nothing that could
    // reach either (#2712). Stacked, the name reaches the one on top, as a person can use only that one.
    [AvaloniaFact]
    public void A_name_shared_by_a_dialog_and_the_box_over_it_reaches_the_box_and_then_the_dialog()
    {
        var main = Shown("HexIDE");
        var find = Track(new Window { Title = "Find" });
        find.Show(main);
        var box = Track(new Window { Title = "Find" });
        _ = box.ShowDialog(find);

        ForegroundWindow.Pick("Find", main, [main, find, box]).Window.Should().BeSameAs(box);

        box.Close();
        ForegroundWindow.Pick("Find", main, [main, find, box]).Window.Should().BeSameAs(find);
    }

    // A path from the dialog fails in the box, and says the name reached the window on top of the one it came from.
    [AvaloniaFact]
    public void A_path_that_fails_in_the_box_says_the_name_reached_the_window_on_top()
    {
        var main = Shown("HexIDE");
        var find = Track(new Window { Title = "Find" });
        find.Show(main);
        var box = Track(new Window { Title = "Find" });
        _ = box.ShowDialog(find);

        var (control, error) = ForegroundWindow.Resolve("Window/Button[Find Next]", "Find", box, main, "Find");

        control.Should().BeNull();
        error.Should().Contain("'Find' reached the window on top, \"Find\", which sits over another window of that name");
        ForegroundWindow.Resolve("Window/Button[Find Next]", "Find", find, main, "Find").Error
            .Should().NotContain("window on top", "the dialog is on top of nothing of its name");
    }
}
