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
    private const string Tail = " dump_visual_tree with 'window' set to its title shows it, and interact answers it.";

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
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    [AvaloniaFact]
    public void A_dialog_the_call_opened_is_named_with_what_it_is_over()
    {
        var ide = Shown(new Window(), "HexIDE");
        var watch = OpenDialogs.Watch([ide], ide, waitsForProgram: false);
        var box = Dialog("Set Next Statement", ide);
        try
        {
            OpenDialogs.Note(watch, [ide, box]).Should()
                .Be("'Set Next Statement', a modal dialog over 'HexIDE', opened during this call and waits for an answer." + Tail);
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
            var watch = OpenDialogs.Watch([ide, addProcedure], ide, waitsForProgram: false);

            OpenDialogs.Note(watch, [ide, addProcedure]).Should().Be(
                "'Add Procedure', a modal dialog over 'HexIDE', is still open and waits for an answer; this call reached past "
                + "it, as a person could not." + Tail);
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
            var watch = OpenDialogs.Watch([ide, options, confirm], confirm, waitsForProgram: false);

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
            var watch = OpenDialogs.Watch([ide, form, box], ide, waitsForProgram: false);

            OpenDialogs.Note(watch, [ide, form, box]).Should().Be(
                "'Question', a modal dialog over the running form 'Ask', is open and waits for an answer." + Tail);
        }
        finally { box.Close(); form.Close(); ide.Close(); }
    }

    // The wait for the program names a box over its form while the program is held on it, as a MsgBox holds it (#1232);
    // a tool that does not wait for it names the box here.
    [AvaloniaFact]
    public void A_held_programs_dialog_is_left_only_to_a_wait_for_that_program()
    {
        var form = Shown(new FormWindow(), "Ask");
        var answer = new TaskCompletionSource();
        try
        {
            var waited = OpenDialogs.Watch([form], form, waitsForProgram: true);
            var notWaited = OpenDialogs.Watch([form], form, waitsForProgram: false);
            form.Events!.Raise("Command1_Click", async () => await form.Events.WhileSuspended(new ValueTask(answer.Task)));
            var box = Dialog("Question", form);
            form.Events.IsIdle.Should().BeFalse("the handler is held on the box, as on a MsgBox");
            try
            {
                OpenDialogs.Note(waited, [form, box]).Should().BeNull();
                OpenDialogs.Note(notWaited, [form, box]).Should()
                    .StartWith("'Question', a modal dialog over the running form 'Ask', opened during this call");
            }
            finally { box.Close(); }
        }
        finally { answer.SetResult(); form.Close(); }
    }

    // A compile error reached from a click shows its dialog over the form without holding the program, so the wait,
    // which returns at idle naming only forms, never named it, and the reply came back clean (#1253).
    [AvaloniaFact]
    public void An_idle_programs_dialog_is_named_though_the_call_waited_for_the_program()
    {
        var form = Shown(new FormWindow(), "Ask");
        try
        {
            var waited = OpenDialogs.Watch([form], form, waitsForProgram: true);
            var box = Dialog("Sub or Function not defined", form);
            form.Events!.IsIdle.Should().BeTrue();
            try
            {
                OpenDialogs.Note(waited, [form, box]).Should().Be("'Sub or Function not defined', a modal dialog over the "
                    + "running form 'Ask', opened during this call and waits for an answer." + Tail);
            }
            finally { box.Close(); }
        }
        finally { form.Close(); }
    }

    [AvaloniaFact]
    public void Each_dialog_open_is_named_in_a_sentence_of_its_own()
    {
        var ide = Shown(new Window(), "HexIDE");
        var form = Shown(new FormWindow(), "Ask");
        var watch = OpenDialogs.Watch([ide, form], ide, waitsForProgram: false);
        var addProcedure = Dialog("Add Procedure", ide);
        var box = Dialog("Question", form);
        try
        {
            OpenDialogs.Note(watch, [ide, form, addProcedure, box]).Should().Be(
                "'Add Procedure', a modal dialog over 'HexIDE', opened during this call and waits for an answer. 'Question', "
                + "a modal dialog over the running form 'Ask', opened during this call and waits for an answer." + Tail);
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
