using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A tool's reply names a modal dialog open once its call is done (#681). Set Next Statement's refusal came back
/// "Invoked" with its message box left open, and run_project succeeded under Add Procedure, a dialog nobody answered.
/// </summary>
public class OpenDialogsTests
{
    private const string Tail = "; dump_visual_tree with 'window' set to its title shows it, and interact answers it.";

    // A running form, as the note tells one from the IDE, with the event queue the wait for its program watches.
    private sealed class FormWindow : Window, IModuleExecutionRoot
    {
        public EventPump? Events { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) { }
    }

    private static T Shown<T>(T window, string title) where T : Window
    {
        window.Title = title;
        window.Show();
        return window;
    }

    private static Window Dialog(string title, Window owner)
    {
        var dialog = new Window { Title = title };
        _ = dialog.ShowDialog(owner);
        RunJobsKeepingContext();
        return dialog;
    }

    // Avalonia 12.0.4 ran each job by restoring the context it was posted from, ExecutionContext.Restore and not Run, and
    // left that context on the thread. So a job posted before this test's OpenDialogs.During, and run here, put back the
    // watch of whatever posted it, and the test's NamedInReply went to that watch instead: a flake that needed the job to
    // still be queued, so it showed under load (#2029). From 12.0.5 a job runs under its context and the thread's own comes
    // back after it (AvaloniaUI/Avalonia#21627), so this helper no longer has anything to undo; it stays as the record of
    // why the test pumps jobs this way. The tools were never exposed to this: HexIDE's own code never pumps the dispatcher
    // inline, and the tool's flow reaches NamedInReply through jobs it posted or awaits it took. OpenDialogNoteTests keeps
    // the same helper for HexIDE.Tests.
    private static void RunJobsKeepingContext()
    {
        var context = ExecutionContext.Capture();
        try
        {
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            if (context is not null)
                ExecutionContext.Restore(context);
        }
    }

    [AvaloniaFact]
    public void Same_title_dialogs_name_their_content_and_actual_buttons()
    {
        var ide = Shown(new Window(), "IDE");
        var watch = OpenDialogs.Watch([ide], ide);
        var message = new Window { Title = "HexIDE", Content = new HexIDE.Runtime.Dialogs.MessageBox
        {
            Text = "This action will reset your project…",
            Buttons = HexIDE.IDE.MessageBoxButtons.YesNo
        } };
        var error = new HexIDE.IDE.DialogWindow { Title = "HexIDE", Content = new HexIDE.Forms.Views.RuntimeErrorView
        {
            DataContext = new HexIDE.Forms.ViewModels.RuntimeErrorViewModel("Run-time error '11': Division by zero")
        } };
        // The headless app has no language pack; supply the view's dynamic resources locally.
        var errorView = (HexIDE.Forms.Views.RuntimeErrorView)error.Content!;
        foreach (var caption in new[] { "Continue", "End", "Debug", "Help" })
            errorView.Resources["Str.Dialog.RuntimeError.Button." + caption] = "_" + caption;
        _ = message.ShowDialog(ide);
        _ = error.ShowDialog(ide);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var messageNote = OpenDialogs.Note(watch, [ide, message]);
            var errorNote = OpenDialogs.Note(watch, [ide, error]);
            using var assertions = new AwesomeAssertions.Execution.AssertionScope();
            messageNote.Should().Contain("'HexIDE' (a message box: \"This action will reset your project…\", Yes/No)");
            errorNote.Should().Contain("'HexIDE' (the run-time error dialog: \"Run-time error '11': Division by zero\", Continue/End/Debug/Help)");
            messageNote.Should().NotBe(errorNote);
        }
        finally { error.Close(); message.Close(); ide.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Message_text_is_one_line_and_bounded(bool multiline)
    {
        var ide = Shown(new Window(), "IDE");
        var watch = OpenDialogs.Watch([ide], ide);
        var text = multiline ? "First line" + Environment.NewLine + "Second line" : new string('x', 140);
        var dialog = new Window { Title = "HexIDE", Content = new HexIDE.Runtime.Dialogs.MessageBox
        {
            Text = text,
            Icon = HexIDE.IDE.MessageBoxIcon.Question
        } };
        _ = dialog.ShowDialog(ide);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var note = OpenDialogs.Note(watch, [ide, dialog]);
            var excerpt = multiline ? "First line…" : new string('x', 99) + "…";
            note.Should().Contain("a message box: \"" + excerpt + "\", OK");
            note.Should().NotContain("Second line");
        }
        finally { dialog.Close(); ide.Close(); }
    }

    [AvaloniaFact]
    public void Unknown_dialog_kind_falls_back_to_title_and_buttons()
    {
        var ide = Shown(new Window(), "IDE");
        var watch = OpenDialogs.Watch([ide], ide);
        var dialog = new Window { Title = "Unfamiliar", Content = new Button { Content = "_Proceed" } };
        _ = dialog.ShowDialog(ide);
        Dispatcher.UIThread.RunJobs();
        try
        {
            OpenDialogs.Note(watch, [ide, dialog]).Should().Contain("'Unfamiliar' (Proceed), a modal dialog");
        }
        finally { dialog.Close(); ide.Close(); }
    }

    // A check box, a radio button and a toggle button are buttons to Avalonia, but none answers the dialog (#2079).
    [AvaloniaFact]
    public void A_dialogs_toggles_are_not_listed_among_its_buttons()
    {
        var ide = Shown(new Window(), "IDE");
        var watch = OpenDialogs.Watch([ide], ide);
        var dialog = new Window { Title = "Options", Content = new StackPanel { Children =
        {
            new CheckBox { Content = "Prompt for project" },
            new RadioButton { Content = "Public" },
            new Avalonia.Controls.Primitives.ToggleButton { Content = "Advanced" },
            new Button { Content = "OK" },
            new Button { Content = "Cancel" },
        } } };
        _ = dialog.ShowDialog(ide);
        Dispatcher.UIThread.RunJobs();
        try
        {
            OpenDialogs.Note(watch, [ide, dialog]).Should().StartWith("'Options' (OK/Cancel), a modal dialog");
        }
        finally { dialog.Close(); ide.Close(); }
    }

    [AvaloniaFact]
    public void A_dialog_the_call_opened_is_named_with_what_it_is_over()
    {
        var ide = Shown(new Window(), "HexIDE");
        var watch = OpenDialogs.Watch([ide], ide);
        var box = Dialog("Set Next Statement", ide);
        try
        {
            OpenDialogs.Note(watch, [ide, box]).Should()
                .Be("'Set Next Statement', a modal dialog over 'HexIDE', opened during this call and waits for an answer" + Tail);
        }
        finally { box.Close(); ide.Close(); }
    }

    [AvaloniaFact]
    public void A_dialog_already_open_over_where_the_call_acted_says_the_call_reached_behind_it()
    {
        var ide = Shown(new Window(), "HexIDE");
        var addProcedure = Dialog("Add Procedure", ide);
        try
        {
            var watch = OpenDialogs.Watch([ide, addProcedure], ide);

            OpenDialogs.Note(watch, [ide, addProcedure]).Should().Be(
                "'Add Procedure', a modal dialog over 'HexIDE', is still open and waits for an answer; this call reached past "
                + "it, as a person could not" + Tail);
        }
        finally { addProcedure.Close(); ide.Close(); }
    }

    // A window tool left to the frontmost window, or sent to the dialog by its title, acts in the dialog: nothing to tell.
    [AvaloniaFact]
    public void The_dialog_the_call_acted_in_is_not_named_nor_one_under_it()
    {
        var ide = Shown(new Window(), "HexIDE");
        var options = Dialog("Options", ide);
        var confirm = Dialog("Confirm", options);
        try
        {
            var watch = OpenDialogs.Watch([ide, options, confirm], confirm);

            OpenDialogs.Note(watch, [ide, options, confirm]).Should().BeNull();
        }
        finally { confirm.Close(); options.Close(); ide.Close(); }
    }

    // A program's MsgBox over its form does not stop a person using the IDE.
    [AvaloniaFact]
    public void A_dialog_over_another_window_is_named_without_saying_the_call_reached_behind_it()
    {
        var ide = Shown(new Window(), "HexIDE");
        var form = Shown(new FormWindow(), "Ask");
        var box = Dialog("Question", form);
        try
        {
            var watch = OpenDialogs.Watch([ide, form, box], ide);

            OpenDialogs.Note(watch, [ide, form, box]).Should().Be(
                "'Question', a modal dialog over the running form 'Ask', is open and waits for an answer" + Tail);
        }
        finally { box.Close(); form.Close(); ide.Close(); }
    }

    // The wait for the program names a box over its form while the program is held on it, as a MsgBox holds it (#1232),
    // and records it, so the note leaves out that box and no other.
    [AvaloniaFact]
    public void A_dialog_the_wait_named_is_left_out_of_the_note_and_only_that_one()
    {
        var form = Shown(new FormWindow(), "Ask");
        var earlier = Dialog("HexIDE", form);
        try
        {
            var waited = OpenDialogs.Watch([form, earlier], form);
            var other = OpenDialogs.Watch([form, earlier], form);
            OpenDialogs.During(waited);
            var box = Dialog("Question", form);
            try
            {
                OpenDialogs.NamedInReply(box);

                OpenDialogs.Note(waited, [form, earlier, box]).Should().Be("'HexIDE', a modal dialog over the running form "
                    + "'Ask', is still open and waits for an answer; this call reached past it, as a person could not" + Tail);
                OpenDialogs.Note(other, [form, earlier, box]).Should().Contain("'Question'");
            }
            finally { box.Close(); }
        }
        finally { earlier.Close(); form.Close(); }
    }

    // The flake in #2029, made certain: a job posted under another watch is still queued when the dialog opens, and running
    // it must not take this test's watch away before NamedInReply records the box against it.
    [AvaloniaFact]
    public void A_job_posted_under_another_watch_does_not_take_this_one_away()
    {
        var form = Shown(new FormWindow(), "Ask");
        RunJobsKeepingContext();
        try
        {
            // Posted under another watch, and at the lowest priority a pump runs, so it runs last and its context is the one
            // left on the thread.
            var ran = false;
            OpenDialogs.During(OpenDialogs.Watch([form], form));
            Dispatcher.UIThread.Post(() => ran = true, DispatcherPriority.SystemIdle);
            var waited = OpenDialogs.Watch([form], form);
            OpenDialogs.During(waited);
            var box = Dialog("Question", form);
            try
            {
                ran.Should().BeTrue("the job posted under the other watch must have run for this to test anything");
                OpenDialogs.NamedInReply(box);

                OpenDialogs.Note(waited, [form, box]).Should().BeNull("the box was named in the reply, so the note leaves it out");
            }
            finally { box.Close(); }
        }
        finally { form.Close(); }
    }

    // A compile error reached from a click shows its dialog over the form without holding the program, so the wait,
    // which returns at idle naming only forms, never named it, and the reply came back clean (#1253). Whether the program
    // is busy by the time the note looks, as a Timer's tick makes it, is no part of it: only what the wait named counts.
    [AvaloniaFact]
    public void A_dialog_the_wait_did_not_name_is_named_though_the_program_is_busy_when_the_note_looks()
    {
        var form = Shown(new FormWindow(), "Ask");
        var tick = new TaskCompletionSource();
        try
        {
            var waited = OpenDialogs.Watch([form], form);
            OpenDialogs.During(waited);
            var box = Dialog("Sub or Function not defined", form);
            form.Events!.Raise("Timer1_Timer", async () => await form.Events.WhileSuspended(new ValueTask(tick.Task)));
            form.Events.IsIdle.Should().BeFalse("a tick is under way");
            try
            {
                OpenDialogs.Note(waited, [form, box]).Should().Be("'Sub or Function not defined', a modal dialog over the "
                    + "running form 'Ask', opened during this call and waits for an answer" + Tail);
            }
            finally { box.Close(); }
        }
        finally { tick.SetResult(); form.Close(); }
    }

    [AvaloniaFact]
    public void Each_dialog_open_is_named_in_one_note()
    {
        var ide = Shown(new Window(), "HexIDE");
        var form = Shown(new FormWindow(), "Ask");
        var watch = OpenDialogs.Watch([ide, form], ide);
        var addProcedure = Dialog("Add Procedure", ide);
        var box = Dialog("Question", form);
        try
        {
            OpenDialogs.Note(watch, [ide, form, addProcedure, box]).Should().Be(
                "'Add Procedure', a modal dialog over 'HexIDE', opened during this call and waits for an answer; 'Question', "
                + "a modal dialog over the running form 'Ask', opened during this call and waits for an answer" + Tail);
        }
        finally { box.Close(); addProcedure.Close(); form.Close(); ide.Close(); }
    }

    // Over a hidden form, as from Form_Load, a program's MsgBox waits in a window of its own, not shown as a dialog.
    [AvaloniaFact]
    public void A_programs_box_shown_in_a_window_of_its_own_counts()
    {
        var box = Shown(new Window { Content = new HexIDE.Runtime.Dialogs.MessageBox() }, "Ask");
        try
        {
            OpenDialogs.Among([box]).Should().Equal(box);
        }
        finally { box.Close(); }
    }

    [AvaloniaFact]
    public void A_window_that_is_not_modal_is_not_counted()
    {
        var ide = Shown(new Window(), "HexIDE");
        var tool = new Window { Title = "Find" };
        tool.Show(ide);
        try
        {
            OpenDialogs.Among([ide, tool]).Should().BeEmpty();
        }
        finally { tool.Close(); ide.Close(); }
    }
}
