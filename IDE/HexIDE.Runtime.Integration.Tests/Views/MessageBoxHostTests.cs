using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using MessageBox = HexIDE.Runtime.Dialogs.MessageBox;

namespace HexIDE.Runtime.Integration.Tests.Views;

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

    // #827: docs/vb6-fidelity-oracle/rgb-qbcolor-and-msgboxs-style.md, Enter/Escape table.
    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.Ok, 0, MessageBoxLayout.None, 0, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.Ok, 256, MessageBoxLayout.SecondButtonDefault, 0, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.Ok, 512, MessageBoxLayout.None, 0, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.Ok, 768, MessageBoxLayout.None, 0, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.OkCancel, 0, MessageBoxLayout.None, 0, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.OkCancel, 256, MessageBoxLayout.SecondButtonDefault, 1, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.OkCancel, 512, MessageBoxLayout.None, 0, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.OkCancel, 768, MessageBoxLayout.None, 0, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, 0, MessageBoxLayout.None, 0, MessageBoxResult.Abort)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, 256, MessageBoxLayout.SecondButtonDefault, 1, MessageBoxResult.Retry)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, 512, MessageBoxLayout.ThirdButtonDefault, 2, MessageBoxResult.Ignore)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, 768, MessageBoxLayout.None, 0, MessageBoxResult.Abort)]
    [InlineData(MessageBoxButtons.YesNoCancel, 0, MessageBoxLayout.None, 0, MessageBoxResult.Yes)]
    [InlineData(MessageBoxButtons.YesNoCancel, 256, MessageBoxLayout.SecondButtonDefault, 1, MessageBoxResult.No)]
    [InlineData(MessageBoxButtons.YesNoCancel, 512, MessageBoxLayout.ThirdButtonDefault, 2, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.YesNoCancel, 768, MessageBoxLayout.None, 0, MessageBoxResult.Yes)]
    [InlineData(MessageBoxButtons.YesNo, 0, MessageBoxLayout.None, 0, MessageBoxResult.Yes)]
    [InlineData(MessageBoxButtons.YesNo, 256, MessageBoxLayout.SecondButtonDefault, 1, MessageBoxResult.No)]
    [InlineData(MessageBoxButtons.YesNo, 512, MessageBoxLayout.None, 0, MessageBoxResult.Yes)]
    [InlineData(MessageBoxButtons.YesNo, 768, MessageBoxLayout.None, 0, MessageBoxResult.Yes)]
    [InlineData(MessageBoxButtons.RetryCancel, 0, MessageBoxLayout.None, 0, MessageBoxResult.Retry)]
    [InlineData(MessageBoxButtons.RetryCancel, 256, MessageBoxLayout.SecondButtonDefault, 1, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.RetryCancel, 512, MessageBoxLayout.None, 0, MessageBoxResult.Retry)]
    [InlineData(MessageBoxButtons.RetryCancel, 768, MessageBoxLayout.None, 0, MessageBoxResult.Retry)]
    public void Enter_answers_the_focused_default(MessageBoxButtons buttons, int bits, MessageBoxLayout layout,
        int index, MessageBoxResult expected) => CheckKey(buttons, layout, Key.Enter, index, expected, bits);

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.Ok, 0, MessageBoxLayout.None, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.Ok, 512, MessageBoxLayout.None, MessageBoxResult.Ok)]
    [InlineData(MessageBoxButtons.OkCancel, 0, MessageBoxLayout.None, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.OkCancel, 512, MessageBoxLayout.None, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, 0, MessageBoxLayout.None, MessageBoxResult.None)]
    [InlineData(MessageBoxButtons.AbortRetryIgnore, 512, MessageBoxLayout.ThirdButtonDefault, MessageBoxResult.None)]
    [InlineData(MessageBoxButtons.YesNoCancel, 0, MessageBoxLayout.None, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.YesNoCancel, 512, MessageBoxLayout.ThirdButtonDefault, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.YesNo, 0, MessageBoxLayout.None, MessageBoxResult.None)]
    [InlineData(MessageBoxButtons.YesNo, 512, MessageBoxLayout.None, MessageBoxResult.None)]
    [InlineData(MessageBoxButtons.RetryCancel, 0, MessageBoxLayout.None, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButtons.RetryCancel, 512, MessageBoxLayout.None, MessageBoxResult.Cancel)]
    public void Escape_answers_as_close(MessageBoxButtons buttons, int bits, MessageBoxLayout layout, MessageBoxResult expected) =>
        CheckKey(buttons, layout, Key.Escape, null, expected, bits);

    private static void CheckKey(MessageBoxButtons buttons, MessageBoxLayout layout, Key key,
        int? index, MessageBoxResult expected, int bits)
    {
        var owner = new Window();
        owner.Show();
        var answer = new StandaloneStandardLib(owner, HexIDE.IDE.Distribution.DisplayName)
            .MsgBox("probe", "Key probe", buttons, MessageBoxIcon.None, layout);
        var window = owner.OwnedWindows.Single();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.Activate();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            if (index is { } i)
                window.FocusManager!.GetFocusedElement().Should().Be(
                    window.GetVisualDescendants().OfType<Button>().ElementAt(i));
            var physical = key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape;
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
            answer.IsCompleted.Should().BeFalse($"style {bits}: a down without its release cannot answer");
            window.KeyRelease(key, RawInputModifiers.None, physical, null);
            Dispatcher.UIThread.RunJobs();
            if (expected == MessageBoxResult.None)
                answer.IsCompleted.Should().BeFalse();
            else
            {
                answer.IsCompleted.Should().BeTrue();
                answer.Result.Should().Be(expected);
            }
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    }

    private static void Press(MessageBox box, string label)
    {
        // On Windows the captions are the platform's, with its mnemonic as an access key ("_No"); #4250.
        var button = box.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content is string text && text.Replace("_", "") == label);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(MessageBoxButtons.YesNo, "No", MessageBoxResult.No)]
    [InlineData(MessageBoxButtons.OkCancel, "OK", MessageBoxResult.Ok)]
    public void A_box_whose_owner_is_already_hidden_is_shown_on_its_own_and_waits_for_its_answer(
        MessageBoxButtons buttons, string press, MessageBoxResult expected)
    {
        // ShowDialog refuses a hidden owner ("Cannot show window with non-visible owner"), which ended the run as
        // Internal error 51. VB6 shows nothing and keeps waiting (measured), as when the owner hides while it waits.
        var owner = new Window { Width = 300, Height = 200 };
        owner.Show();
        owner.Hide();
        var box = new MessageBox { Text = "probe", Buttons = buttons };
        var window = new Window { Content = box, Width = 200, Height = 100 };

        var answer = box.ShowAndWait(window, owner);
        Dispatcher.UIThread.RunJobs();

        answer.IsCompleted.Should().BeFalse("the box is waiting for an answer");
        var shownIn = TopLevel.GetTopLevel(box).Should().BeOfType<Window>().Subject;
        shownIn.IsVisible.Should().BeTrue("a box nobody can see could never be answered");
        shownIn.Owner.Should().BeNull();

        Press(box, press);
        answer.IsCompleted.Should().BeTrue();
        answer.Result.Should().Be(expected);
        owner.Close();
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
