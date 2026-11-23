using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Automation;
using HexIDE.Utils;

namespace HexIDE.Integration.Tests.Automation;

public class InvokeSettledTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task An_async_command_opens_a_dialog_during_the_call(bool relay, bool menu)
    {
        Window? dialog = null;
        var owner = new Window { Title = "IDE" };
        async Task Run()
        {
            await Task.Delay(100);
            dialog = new Window { Title = "Continue?" };
            await dialog.ShowDialog(owner);
        }
        ICommand command = relay ? new AsyncRelayCommand(Run) : new DelegateCommand(Run);
        Control target = menu ? new MenuItem { Header = "Find Next", Command = command }
            : new Button { Content = "Find Next", Command = command };
        owner.Content = target;
        owner.Show();
        var watch = OpenDialogs.Watch([owner], owner);
        try
        {
            var result = await UiAutomationDriver.InteractAsync(target, "invoke", null);
            result.Success.Should().BeTrue(result.Error);
            dialog.Should().NotBeNull();
            OpenDialogs.Note(watch, [owner, dialog!]).Should().Contain("opened during this call");
            result.Detail.Should().NotContain("dialog").And.NotContain("took longer");
        }
        finally { dialog?.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task A_running_command_finishes_even_with_an_unrelated_dialog_already_open()
    {
        var finished = false;
        async Task Run()
        {
            await Task.Delay(100);
            finished = true;
        }
        var button = new Button { Content = "Find Next", Command = new DelegateCommand(Run) };
        var owner = new Window { Content = button };
        owner.Show();
        var other = new Window();
        other.Show();
        var oldDialog = new Window { Title = "Already open" };
        _ = oldDialog.ShowDialog(other);
        var watch = OpenDialogs.Watch([owner, other, oldDialog], owner);
        try
        {
            var result = await UiAutomationDriver.InteractAsync(button, "invoke", null);
            result.Success.Should().BeTrue(result.Error);
            finished.Should().BeTrue();
            OpenDialogs.Note(watch, [owner, other, oldDialog]).Should().Contain("is open")
                .And.NotContain("opened during this call");
        }
        finally { oldDialog.Close(); other.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task Work_that_outlasts_the_budget_is_named_and_does_not_hold_the_call()
    {
        var pending = new TaskCompletionSource();
        var button = new Button { Content = "Wait", Command = new DelegateCommand(() => pending.Task) };
        var owner = new Window { Content = button };
        owner.Show();
        var watch = OpenDialogs.Watch([owner], owner);
        try
        {
            var elapsed = Stopwatch.StartNew();
            var result = await UiAutomationDriver.InteractAsync(button, "invoke", null);
            elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
            result.Detail.Should().EndWith("; replied before the work it set off had finished, which took longer than 2000 ms");
            OpenDialogs.Note(watch, [owner]).Should().BeNull();
        }
        finally { pending.TrySetResult(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task A_synchronous_command_keeps_its_reply_and_waits_for_one_dispatcher_pass()
    {
        var posted = false;
        var button = new Button { Content = "Find Next", Command = new DelegateCommand(() =>
            Dispatcher.UIThread.Post(() => posted = true, DispatcherPriority.Background)) };
        var owner = new Window { Content = button };
        owner.Show();
        var watch = OpenDialogs.Watch([owner], owner);
        try
        {
            var elapsed = Stopwatch.StartNew();
            var result = await UiAutomationDriver.InteractAsync(button, "invoke", null);
            result.Detail.Should().Be("invoked Button 'Find Next'");
            posted.Should().BeTrue();
            elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
            OpenDialogs.Note(watch, [owner]).Should().BeNull();
        }
        finally { owner.Close(); }
    }
}
