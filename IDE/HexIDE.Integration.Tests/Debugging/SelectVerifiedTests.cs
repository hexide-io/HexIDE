using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task Selecting_a_caller_row_by_its_path_moves_the_debugger_to_that_frame()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-dap-select-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new ConversationLog();
        var service = new ExternalDebugService(log);
        var stack = new CallStackToolViewModel(
            NSubstitute.Substitute.For<ILocalizationService>(), NSubstitute.Substitute.For<IDebugController>(), service);
        var window = new Window { Width = 300, Height = 300, Content = new CallStackToolView { DataContext = stack } };
        window.Show();
        var run = service.StartAsync(await Configuration(directory, "frames"));
        try
        {
            await Until(() => service.Variables.Count == 1 && stack.Frames.Count == 2);

            // Named as the row reads, with nothing in it that changes from one stop to the next.
            var rows = Flatten(UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true))
                .Where(n => n.Path.Contains("ListItem")).ToList();
            rows.Select(n => n.Name).Should().Equal("Callee, line 6", "Caller, line 12");
            var caller = UiAutomationDriver.Resolve(window, rows[1].Path).control!;

            var outcome = UiAutomationDriver.Interact(caller, "select", null);
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("selected 'Caller, line 12'");
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
        Dispatcher.UIThread.RunJobs();
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
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = UiAutomationDriver.Interact(stray, "select", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith("'Main' was not selected");
        }
        finally { window.Close(); }
    }
}
