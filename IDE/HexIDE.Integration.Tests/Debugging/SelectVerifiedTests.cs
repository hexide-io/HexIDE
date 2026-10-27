using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Conversations;
using HexIDE.Debugging;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Debugging;

/// <summary>
/// select reads the selection back before it reports one (#1182). A select on a Call Stack row once said it had chosen
/// the caller's frame while the debugger stayed in the callee, and a row's path carried the debug session's id and stop
/// generation, so it went stale at every stop.
/// </summary>
public class SelectVerifiedTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    private static IEnumerable<UiNode> Flatten(UiNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    // The fixture adapter HexIDE.Tests drives external sessions with: "frames" stops in Callee (frame 0) under Caller (7).
    private static async Task<string> Configuration(string directory, string mode)
    {
        var executable = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""),
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var file = Path.Combine(directory, "launch.json");
        await File.WriteAllTextAsync(file, System.Text.Json.JsonSerializer.Serialize(new
        {
            executable, arguments = new[] { typeof(Dap.TestAdapter.Program).Assembly.Location, mode },
            adapterId = "fixture", launch = new { }, breakpoints = Array.Empty<object>(),
        }));
        return file;
    }

    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!done())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the fixture session answers well within this");
            RunJobsKeepingContext();
            await Task.Delay(10);
        }
    }

    // A dispatcher job restores its posting context and leaves it on this thread. Preserve the test's context so
    // later awaits and xunit cleanup cannot inherit an idle context from a job queued by another test (#1296).
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
    public void Draining_jobs_keeps_the_tests_execution_context()
    {
        var marker = new AsyncLocal<string>();
        marker.Value = "queued job";
        Dispatcher.UIThread.Post(() => marker.Value.Should().Be("queued job"));
        marker.Value = "current test";

        RunJobsKeepingContext();

        marker.Value.Should().Be("current test");
    }

    private static ILocalizationService Localization(string format = "{0}, line {1}")
    {
        var localization = NSubstitute.Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.CallStack.RowName").Returns(format);
        return localization;
    }

    [AvaloniaTheory]
    [InlineData("{0}, line {1}", "Callee, line 6", "Caller, line 12")]
    [InlineData("{0}, Zeile {1}", "Callee, Zeile 6", "Caller, Zeile 12")]
    public async Task Selecting_a_caller_row_by_its_path_moves_the_debugger_to_that_frame(
        string format, string calleeName, string callerName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-select-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var stack = new CallStackToolViewModel(
            Localization(format), NSubstitute.Substitute.For<IDebugController>(), service);
        var window = new Window { Width = 300, Height = 300, Content = new CallStackToolView { DataContext = stack } };
        window.Show();
        var run = service.StartAsync(await Configuration(directory, "frames"));
        try
        {
            await Until(() => service.Variables.Count == 1 && stack.Frames.Count == 2);

            // Named as the row reads, with nothing in it that changes from one stop to the next.
            var rows = Flatten(UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true))
                .Where(n => n.Path.Contains("ListItem")).ToList();
            rows.Select(n => n.Name).Should().Equal(calleeName, callerName);
            stack.Frames.Select(row => row.ToString()).Should().Equal(calleeName, callerName);
            var caller = UiAutomationDriver.Resolve(window, rows[1].Path).control!;

            var outcome = UiAutomationDriver.Interact(caller, "select", null);
            RunJobsKeepingContext();

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be($"selected '{callerName}'");
            await Until(() => service.Variables.Count == 1);
            service.SelectedFrame!.Value.GetProperty("id").GetInt32().Should().Be(7);
        }
        finally
        {
            window.Close();
            await service.StopAsync();
            await run.WaitAsync(Patience);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Selecting_a_row_of_a_stop_the_debugger_has_left_is_refused_and_the_list_goes_back()
    {
        // The list took the row and the debugger declined it, and the reply said the frame was chosen (#1182 review).
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-select-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var stack = new CallStackToolViewModel(
            Localization(), NSubstitute.Substitute.For<IDebugController>(), service);
        var window = new Window { Width = 300, Height = 300, Content = new CallStackToolView { DataContext = stack } };
        window.Show();
        var run = service.StartAsync(await Configuration(directory, "frames"));
        try
        {
            await Until(() => service.Variables.Count == 1 && stack.Frames.Count == 2);
            var rows = Flatten(UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true))
                .Where(n => n.Path.Contains("ListItem")).ToList();
            var caller = UiAutomationDriver.Resolve(window, rows[1].Path).control!;
            var stop = service.Stop;

            // Stepping moves the session on at once; the rows of the stop it left are still showing.
            var stepping = service.CommandAsync("next");
            service.CanSelectFrame(stop!, 7).Should().BeFalse("the premise: the session has left that stop");
            var outcome = UiAutomationDriver.Interact(caller, "select", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("'Caller, line 12' was not selected");
            stack.SelectedFrame?.FrameId.Should().NotBe(7);
            await stepping;
        }
        finally
        {
            window.Close();
            await service.StopAsync();
            await run.WaitAsync(Patience);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void A_recursive_call_s_deeper_row_is_selected_by_its_path()
    {
        // The same procedure and line twice: the rows were equal records, and the list selected the first for the second.
        var controller = NSubstitute.Substitute.For<IDebugController>();
        controller.GetCallStack().Returns(new[]
        {
            new CallStackFrame("Fact", "Module1", 3), new CallStackFrame("Fact", "Module1", 5),
            new CallStackFrame("Fact", "Module1", 5), new CallStackFrame("Main", "Module1", 10),
        });
        var stack = new CallStackToolViewModel(Localization(), controller);
        var window = new Window { Width = 300, Height = 300, Content = new CallStackToolView { DataContext = stack } };
        window.Show();
        controller.Stopped += NSubstitute.Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Breakpoint, "Module1", 3));
        RunJobsKeepingContext();
        try
        {
            var deeper = window.GetVisualDescendants().OfType<ListBoxItem>().Single(i => ReferenceEquals(i.DataContext, stack.Frames[2]));

            var outcome = UiAutomationDriver.Interact(deeper, "select", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            stack.SelectedFrame.Should().BeSameAs(stack.Frames[2]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_list_that_puts_its_selection_back_is_reported_as_not_selected(bool byValue)
    {
        var list = new ListBox { ItemsSource = new[] { "Worker", "Main" }, SelectedIndex = 0 };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedIndex != 0) Dispatcher.UIThread.Invoke(() => list.SelectedIndex = 0);
        };
        var window = new Window { Width = 200, Height = 200, Content = list };
        window.Show();
        RunJobsKeepingContext();
        try
        {
            var main = list.ContainerFromIndex(1)!;
            var outcome = byValue
                ? UiAutomationDriver.Interact(list, "select", "Main")
                : UiAutomationDriver.Interact(main, "select", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("'Main' was not selected: its list did not take the selection; "
                                             + "1 item is selected now: Worker");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_row_its_list_cannot_place_is_reported_as_not_selected()
    {
        // The provider's Select asks the item's list for the item's index, and does nothing when there is none.
        var stray = new ListBoxItem { Content = "Main" };
        var window = new Window { Width = 200, Height = 200, Content = new StackPanel { Children = { stray } } };
        window.Show();
        RunJobsKeepingContext();
        try
        {
            var outcome = UiAutomationDriver.Interact(stray, "select", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("'Main' was not selected");
        }
        finally { window.Close(); }
    }
}
