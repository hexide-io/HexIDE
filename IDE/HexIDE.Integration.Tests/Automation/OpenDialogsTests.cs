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
    // A running form, as the note tells one from the IDE.
    private sealed class FormWindow : Window, IModuleExecutionRoot
    {
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) { }
    }

    private static Window Shown(string title)
    {
        var window = new Window { Title = title };
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
        var ide = Shown("HexIDE");
        var before = OpenDialogs.Among([ide]);
        var box = Dialog("HexIDE - Set Next Statement", ide);
        try
        {
            var note = OpenDialogs.Note(before, OpenDialogs.Among([ide, box]), namesOneAlreadyOpen: false);

            note.Should().Be("This call opened a modal dialog, 'HexIDE - Set Next Statement', over 'HexIDE'. Until it is "
                             + "answered a person can use nothing behind it: dump_visual_tree {} shows it, and interact "
                             + "answers it.");
        }
        finally { box.Close(); ide.Close(); }
    }

    [AvaloniaFact]
    public void A_dialog_already_open_is_named_only_where_the_tool_does_not_address_a_window()
    {
        var ide = Shown("HexIDE");
        var addProcedure = Dialog("Add Procedure", ide);
        try
        {
            var open = OpenDialogs.Among([ide, addProcedure]);

            OpenDialogs.Note(open, open, namesOneAlreadyOpen: true).Should().Be(
                "A modal dialog, 'Add Procedure', over 'HexIDE' was open before this call and still is, so the call "
                + "acted on what lies behind it, which a person could not reach. Until it is answered a person can use "
                + "nothing behind it: dump_visual_tree {} shows it, and interact answers it.");
            OpenDialogs.Note(open, open, namesOneAlreadyOpen: false).Should().BeNull();
        }
        finally { addProcedure.Close(); ide.Close(); }
    }

    [AvaloniaFact]
    public void A_window_that_is_not_modal_is_not_named()
    {
        var ide = Shown("HexIDE");
        var tool = new Window { Title = "Find" };
        tool.Show(ide);
        try
        {
            OpenDialogs.Among([ide, tool]).Should().BeEmpty();
        }
        finally { tool.Close(); ide.Close(); }
    }

    // A program's MsgBox is named by the wait for the program that the tools acting on its form make (#1232).
    [AvaloniaFact]
    public void A_programs_dialog_is_left_to_the_tools_that_wait_for_the_program()
    {
        var form = new FormWindow { Title = "Ask" };
        form.Show();
        var before = OpenDialogs.Among([form]);
        var box = Dialog("Question", form);
        try
        {
            var after = OpenDialogs.Among([form, box]);

            OpenDialogs.Note(before, after, namesOneAlreadyOpen: false, leavesProgramDialogs: true).Should().BeNull();
            OpenDialogs.Note(before, after, namesOneAlreadyOpen: true).Should()
                .StartWith("This call opened a modal dialog, 'Question', over the running form 'Ask'.");
        }
        finally { box.Close(); form.Close(); }
    }
}
