using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using MessageBox = HexIDE.Runtime.Dialogs.MessageBox;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A program's message box keeps waiting for an answer until it gets one or the run ends (#820).
/// </summary>
public class MessageBoxHostTests
{
    private static (Window Owner, Window BoxWindow, MessageBox Box, Task<MessageBoxResult> Answer) Show(
        MessageBoxButtons buttons)
    {
        var owner = new Window { Width = 300, Height = 200 };
        owner.Show();
        var box = new MessageBox { Text = "probe", Buttons = buttons };
        var window = new Window { Content = box, Width = 200, Height = 100 };
        var answer = box.ShowAndWait(window, owner);
        Dispatcher.UIThread.RunJobs();
        return (owner, window, box, answer);
    }

    private static void Press(MessageBox box, string label)
    {
        var button = box.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.YesNo, "No", MessageBoxResult.No)]
    [InlineData(MessageBoxButtons.OkCancel, "OK", MessageBoxResult.Ok)]
    public void A_box_hidden_with_its_owner_is_shown_again_and_still_waits_for_its_answer(
        MessageBoxButtons buttons, string press, MessageBoxResult expected)
    {
        // Hiding the owner (Me.Visible = False) hides the box too, and Avalonia then completed the dialog with no
        // answer: OK/Cancel answered Cancel and a Yes/No handler stopped. VB6 keeps the box waiting (measured).
        var (owner, window, box, answer) = Show(buttons);

        owner.Hide();
        Dispatcher.UIThread.RunJobs();

        answer.IsCompleted.Should().BeFalse("the box is still waiting for an answer");
        var shownIn = TopLevel.GetTopLevel(box).Should().BeOfType<Window>().Subject;
        shownIn.IsVisible.Should().BeTrue("a box nobody can see could never be answered");
        shownIn.Owner.Should().BeNull("an owned window stays hidden while its owner is");
        window.IsVisible.Should().BeFalse("the hidden window it was in is closed");

        Press(box, press);
        answer.IsCompleted.Should().BeTrue();
        answer.Result.Should().Be(expected);
        owner.Close();
    }

    [AvaloniaFact]
    public void A_box_shown_again_is_closed_unanswered_when_its_owner_closes()
    {
        var (owner, _, box, answer) = Show(MessageBoxButtons.YesNo);
        owner.Hide();
        Dispatcher.UIThread.RunJobs();
        var shownIn = (Window)TopLevel.GetTopLevel(box)!;

        owner.Close();
        Dispatcher.UIThread.RunJobs();

        answer.IsCompleted.Should().BeTrue("the run that owned it is ending");
        answer.Result.Should().Be(MessageBoxResult.None);
        shownIn.IsVisible.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.YesNo)]
    [InlineData(MessageBoxButtons.OkCancel)]
    public void HexIDEs_own_close_goes_through_and_answers_nothing(MessageBoxButtons buttons)
    {
        // A programmatic close is how ending the run reaches an open box: never refused, and never taken for an answer.
        var (owner, window, _, answer) = Show(buttons);

        window.Close();
        Dispatcher.UIThread.RunJobs();

        answer.IsCompleted.Should().BeTrue();
        answer.Result.Should().Be(MessageBoxResult.None);
        owner.Close();
    }
}
