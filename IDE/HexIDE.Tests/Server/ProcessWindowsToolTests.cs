using System.Diagnostics;
using HexIDE.Desktop.Server;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Tests.Server;

/// <summary>
/// <c>get_process_windows</c> reads the windows of a program a VB6 program started with Shell, and of nothing else (#2591).
/// Every reply that has no window says which case it is.
/// </summary>
public class ProcessWindowsToolTests
{
    // Through the shell, a process inherits none of the test host's handles. Started directly, a program it starts in turn
    // would, and if a failing test left that running, it would hold the test run's output open and the run would wait.
    private static Process Launched(string program, string arguments, ProcessWindowStyle style) =>
        Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = true, WindowStyle = style })!;

    // No window and no children, so killing it leaves nothing behind.
    private static Process Waiting() => OperatingSystem.IsWindows()
        ? Process.Start(new ProcessStartInfo("ping.exe", "-n 30 127.0.0.1") { CreateNoWindow = true })!
        : Process.Start(new ProcessStartInfo("sleep", "30"))!;

    [Fact]
    public async Task A_process_Shell_did_not_start_is_refused_and_the_ones_it_did_are_named()
    {
        using var process = Waiting();
        try
        {
            ShellLaunches.Record(process.Id, "waiting", 0);

            var reply = await ProcessWindowsTool.DescribeAsync(int.MaxValue - 7, TestContext.Current.CancellationToken);

            reply.Success.Should().BeFalse();
            reply.Error.Should().StartWith($"Process {int.MaxValue - 7} wasn't started with Shell by a program run in this IDE")
                .And.Contain($"{process.Id} (waiting)");
        }
        finally { process.Kill(); }
    }

    [Fact]
    public async Task Without_a_pid_the_processes_started_are_listed_with_their_style_and_whether_they_run()
    {
        using var process = Waiting();
        try
        {
            ShellLaunches.Record(process.Id, "waiting", 2);

            var reply = await ProcessWindowsTool.DescribeAsync(null, TestContext.Current.CancellationToken);

            reply.Success.Should().BeTrue();
            reply.Note.Should().EndWith("started with Shell, oldest first. Pass one's processId as 'pid' to read its "
                                        + "windows and those of the processes it started.");
            reply.Started.Should().ContainEquivalentOf(new StartedProcess(process.Id, "waiting", 2, "vbMinimizedFocus", true));
        }
        finally { process.Kill(); }
    }

    [Fact]
    public async Task A_process_that_has_exited_says_so_and_that_nothing_it_started_runs()
    {
        var process = Waiting();
        var id = process.Id;
        ShellLaunches.Record(id, "waiting", 0);
        process.Kill();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.Dispose();

        var reply = await ProcessWindowsTool.DescribeAsync(id, TestContext.Current.CancellationToken);

        reply.Note.Should().Contain("It has exited.");
        if (OperatingSystem.IsWindows())
            reply.Note.Should().Contain("No process it started is running, so there is no window to read.");
    }

    // The test process's own ID stands in for one Shell started, with a start time that isn't its own.
    [Fact]
    public async Task An_ID_passed_to_a_process_that_started_later_is_not_read()
    {
        var started = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        ShellLaunches.Record(new ShellLaunches.Launch(Environment.ProcessId, "earlier", 1, started.AddHours(-1)));

        var reply = await ProcessWindowsTool.DescribeAsync(Environment.ProcessId, TestContext.Current.CancellationToken);

        reply.Note.Should().Contain("It has exited, and its ID now belongs to a process that started later, which isn't read.");
        reply.Windows.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task A_process_whose_start_was_never_read_is_not_read()
    {
        ShellLaunches.Record(new ShellLaunches.Launch(Environment.ProcessId, "unread", 1, null));

        var reply = await ProcessWindowsTool.DescribeAsync(Environment.ProcessId, TestContext.Current.CancellationToken);

        reply.Note.Should().EndWith("It had gone before its start time could be read, and a process now has its ID that "
                                    + "can't be told apart from another, so it isn't read.");
    }

    [WindowsOnlyFact]
    public async Task A_running_process_with_no_window_is_said_to_have_none_yet()
    {
        using var process = Waiting();
        try
        {
            ShellLaunches.Record(process.Id, "waiting", 0);

            var reply = await ProcessWindowsTool.DescribeAsync(process.Id, TestContext.Current.CancellationToken);

            reply.Windows.Should().BeEmpty();
            reply.Note.Should().EndWith("No top-level window after 2 s: a program shows its first window some time after it "
                                        + "starts, so ask again.");
        }
        finally { process.Kill(); }
    }

    // A launcher that starts the program and goes, as cmd's start does and Windows 11's notepad.exe does: the window is the
    // child's, and it is found through the process Shell started.
    [WindowsOnlyFact]
    public async Task A_window_of_a_process_the_started_one_started_is_read_through_it()
    {
        using var launcher = Launched("cmd.exe", "/c start \"\" /min notepad.exe & ping -n 4 127.0.0.1 >nul", ProcessWindowStyle.Hidden);
        var started = launcher.StartTime.ToUniversalTime();
        try
        {
            ShellLaunches.Record(launcher.Id, "launcher", 0);

            // The launcher's own hidden console window comes first, so the reply is asked again until the program's is up,
            // as a caller would.
            ProcessWindowsResult reply;
            var deadline = Environment.TickCount64 + 10_000;
            do
                reply = await ProcessWindowsTool.DescribeAsync(launcher.Id, TestContext.Current.CancellationToken);
            while (!(reply.Windows ?? []).Any(w => w.ClassName == "Notepad" && w.Visible) && Environment.TickCount64 < deadline);
            var windows = reply.Windows ?? [];

            reply.Note.Should().MatchRegex(@" \d+ process(es)? it started (is|are) included\.", "notepad, ping and the console host");
            windows.Should().Contain(w => w.ProcessId != launcher.Id && w.State == "Minimized");
        }
        finally
        {
            foreach (var child in OperatingSystem.IsWindows() ? HexIDE.Automation.ProcessWindows.Descendants(launcher.Id, started) : [])
                try { Process.GetProcessById(child).Kill(); } catch (ArgumentException) { }
            try { launcher.Kill(); } catch (InvalidOperationException) { }
        }
    }

    // A hidden window's show state reads Normal, which a first-contact caller took for a window on screen.
    [WindowsOnlyFact]
    public async Task A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it()
    {
        using var process = Launched("notepad.exe", "", ProcessWindowStyle.Hidden);
        try
        {
            ShellLaunches.Record(process.Id, "notepad.exe", 0);

            var reply = await ProcessWindowsTool.DescribeAsync(process.Id, TestContext.Current.CancellationToken);

            reply.Windows.Should().Contain(w => w.ClassName == "Notepad" && w.State == "Hidden" && !w.Visible);
            reply.Note.Should().EndWith(" None is visible, as vbHide starts a program hidden.");
        }
        finally { try { process.Kill(); } catch (InvalidOperationException) { } }
    }

    [WindowsOnlyFact]
    public async Task A_minimized_program_is_read_as_minimized_and_visible()
    {
        using var process = Launched("notepad.exe", "", ProcessWindowStyle.Minimized);
        try
        {
            ShellLaunches.Record(process.Id, "notepad.exe", 6);

            var reply = await ProcessWindowsTool.DescribeAsync(process.Id, TestContext.Current.CancellationToken);

            reply.Success.Should().BeTrue();
            reply.Note.Should().StartWith($"Shell started process {process.Id} with \"notepad.exe\" and window style 6 "
                                          + "(vbMinimizedNoFocus).");
            reply.Windows.Should().Contain(w => w.State == "Minimized" && w.Visible && w.ClassName == "Notepad");
        }
        finally
        {
            // A launcher may have handed off: whatever it started goes too, by ID, never by name.
            foreach (var window in (await ProcessWindowsTool.DescribeAsync(process.Id, TestContext.Current.CancellationToken)).Windows ?? [])
                try { Process.GetProcessById(window.ProcessId).Kill(); } catch (ArgumentException) { }
            try { process.Kill(); } catch (InvalidOperationException) { }
        }
    }
}
