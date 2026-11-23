using System.Linq;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.IDE;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The runtime-error dialog offers only the choices its view model allows (hexide-io/HexIDE#594).
///
/// <para>
/// Each button's command is a view-model method, and Avalonia consults <c>Can{Name}</c> only when it takes one
/// <c>object?</c> parameter. The four took none, so every button was enabled although only End is meant to be.
/// The assertion has to be on the rendered buttons: the view model always answered correctly, and nothing
/// was asking it.
/// </para>
/// </summary>
public class RuntimeErrorViewIntegrationTests
{
    [AvaloniaFact]
    public void Only_End_is_enabled()
    {
        var view = new RuntimeErrorView { DataContext = new RuntimeErrorViewModel("Run-time error '5'") };
        var window = new Window { Content = view };
        window.Show();

        // In the order the view lays them out: Continue, End, Debug, Help.
        var enabled = view.GetLogicalDescendants().OfType<Button>().Select(b => b.IsEffectivelyEnabled).ToList();

        enabled.Should().Equal(false, true, false, false);
        window.Close();
    }

    // Measured in docs/vb6-fidelity-oracle/closing-the-run-time-error-dialog.md (#607).
    private static DialogWindow ShowError(Window owner)
    {
        var vm = new RuntimeErrorViewModel("Run-time error '5'", canDebug: true);
        var dialog = new DialogWindow { DataContext = vm, Content = vm };
        dialog.DataTemplates.Add(new ViewLocator());
        _ = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    [AvaloniaFact]
    public void A_person_s_close_is_refused()
    {
        var owner = new Window();
        owner.Show();
        var dialog = ShowError(owner);
        try
        {
            var outcome = UiAutomationDriver.Interact(dialog, "close", null);
            Dispatcher.UIThread.RunJobs();
            outcome = UiAutomationDriver.AfterClose(dialog, outcome);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().EndWith("; it is still open: the window, or the program behind it, refused, as it may a person's close");
            dialog.IsVisible.Should().BeTrue();
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public void Escape_leaves_the_dialog_open()
    {
        var owner = new Window();
        owner.Show();
        var dialog = ShowError(owner);
        try
        {
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            dialog.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();

            dialog.IsVisible.Should().BeTrue();
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async System.Threading.Tasks.Task End_and_Debug_answer_and_close(bool end)
    {
        var owner = new Window();
        owner.Show();
        var vm = new RuntimeErrorViewModel("Run-time error '5'", canDebug: true);
        var dialog = new DialogWindow { DataContext = vm, Content = vm };
        dialog.DataTemplates.Add(new ViewLocator());
        var answer = dialog.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var buttons = dialog.GetLogicalDescendants().OfType<Button>().ToArray();
            var outcome = UiAutomationDriver.Interact(buttons[end ? 1 : 2], "invoke", null);
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            dialog.IsVisible.Should().BeFalse();
            answer.IsCompleted.Should().BeTrue();
            (await answer).Should().Be(end);
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Code_and_owner_can_still_close_the_dialog(bool closeOwner)
    {
        var owner = new Window();
        owner.Show();
        var dialog = ShowError(owner);
        try
        {
            if (closeOwner) owner.Close();
            else dialog.Close();
            Dispatcher.UIThread.RunJobs();

            dialog.IsVisible.Should().BeFalse();
        }
        finally { dialog.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public void A_refusal_over_the_owner_names_the_run_time_error_dialog()
    {
        var owner = new Window();
        owner.Show();
        var dialog = ShowError(owner);
        try
        {
            var outcome = UiAutomationDriver.Interact(owner, "close", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("the run-time error dialog").And.Contain("Answer the dialog first");
        }
        finally { dialog.Close(); owner.Close(); }
    }
}
