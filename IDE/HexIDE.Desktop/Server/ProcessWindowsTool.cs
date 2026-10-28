using System.Diagnostics;
using System.Globalization;
using HexIDE.Automation;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Desktop.Server;

/// <summary>A process a program started with Shell, as <c>get_process_windows</c> lists it (#2591).</summary>
internal sealed record StartedProcess(int ProcessId, string Command, int WindowStyle, string WindowStyleName, bool Running);

/// <summary>The reply of <c>get_process_windows</c>.</summary>
internal sealed record ProcessWindowsResult(bool Success, string? Error, string? Note = null,
    IReadOnlyList<StartedProcess>? Started = null, IReadOnlyList<ProcessWindow>? Windows = null);

/// <summary>
/// Describes the windows of a program a VB6 program started with <c>Shell</c> (#2591). What <c>Shell</c> does lands in
/// another process's window, which no other tool reaches. Only processes <see cref="ShellLaunches"/> holds, and those they
/// started in turn, are read, so the tool can't be pointed at any other window on the desktop.
/// </summary>
internal static class ProcessWindowsTool
{
    /// <summary>How long a running process with no visible top-level window yet is given to show one: Shell returns
    /// before the program it starts has drawn anything.</summary>
    internal static readonly TimeSpan FirstWindowWait = TimeSpan.FromSeconds(2);

    internal const string Units =
        "bounds are [left, top, width, height] in screen pixels; Windows puts a minimized window at -32000, -32000.";

    internal const string NoForeground =
        " This session has no foreground window, as in a disconnected or locked session, so no window can be said to have "
        + "focus, and isForeground is false throughout.";

    private enum Standing { Running, Exited, Reused, Unverifiable }

    internal static string StyleName(int style) => style switch
    {
        0 => "vbHide",
        1 => "vbNormalFocus",
        2 => "vbMinimizedFocus",
        3 => "vbMaximizedFocus",
        4 => "vbNormalNoFocus",
        6 => "vbMinimizedNoFocus",
        7 => "minimized without focus, which has no VB constant",
        _ => "not a VB window style",
    };

    public static async Task<ProcessWindowsResult> DescribeAsync(int? pid, CancellationToken ct)
    {
        var launches = ShellLaunches.All;
        if (pid is null)
            return launches.Count == 0
                ? new ProcessWindowsResult(true, null,
                    "No program run in this IDE has started anything with Shell, so there is nothing to read. A process "
                    + "Shell starts is listed here, and passing its ID as 'pid' reads its windows.", Started: [])
                : new ProcessWindowsResult(true, null,
                    $"{Count(launches.Count, "process")} started with Shell, oldest first. Pass one's processId as 'pid' "
                    + "to read its windows and those of the processes it started.", Started: [.. launches.Select(Listed)]);

        var launch = launches.LastOrDefault(l => l.ProcessId == pid);
        if (launch is null)
            return new ProcessWindowsResult(false,
                $"Process {Id(pid.Value)} wasn't started with Shell by a program run in this IDE, so it can't be read: only "
                + "those processes, and the processes they started, are. " + (launches.Count == 0
                    ? "None has been started."
                    : "Started: " + string.Join(", ", launches.Select(l => $"{Id(l.ProcessId)} ({l.Command})")) + "."));

        var (standing, holderStartedUtc) = StandingOf(launch);
        var header = $"Shell started process {Id(launch.ProcessId)} with \"{launch.Command}\" and window style "
                     + $"{launch.WindowStyle.ToString(CultureInfo.InvariantCulture)} ({StyleName(launch.WindowStyle)}).";
        IReadOnlyList<StartedProcess> started = [Listed(launch)];

        if (standing == Standing.Unverifiable)
            return new ProcessWindowsResult(true, null,
                header + " It had gone before its start time could be read, and a process now has its ID that can't be told "
                + "apart from another, so it isn't read.", started, []);
        if (!OperatingSystem.IsWindows())
            return new ProcessWindowsResult(true, null,
                header + (standing == Standing.Running ? " It is running." : SelfText(standing))
                + " Another process's windows can be read only on Windows.", started);

        // A reused ID's own children started after it did; those of the process Shell started, before.
        var ids = new List<int>();
        if (standing == Standing.Running)
            ids.Add(launch.ProcessId);
        var deadline = Environment.TickCount64 + (long)FirstWindowWait.TotalMilliseconds;
        while (true)
        {
            var children = ProcessWindows.Descendants(launch.ProcessId, launch.StartedUtc,
                standing == Standing.Reused ? holderStartedUtc : null);
            var reading = ids.Concat(children).Distinct().ToList();
            var windows = ProcessWindows.Of(reading, out var noForeground);
            // A window is made hidden and shown a moment later, so one caught hidden is waited on like none at all.
            if (windows.Any(w => w.Visible) || reading.Count == 0 || Environment.TickCount64 >= deadline)
                return Reply(header, launch.WindowStyle, standing, children.Count, windows, noForeground, started);
            await Task.Delay(100, ct);
        }
    }

    private static ProcessWindowsResult Reply(string header, int windowStyle, Standing standing, int children,
        IReadOnlyList<ProcessWindow> windows, bool noForeground, IReadOnlyList<StartedProcess> started)
    {
        var self = SelfText(standing);
        var family = children == 0 ? "" : $" {Count(children, "process")} it started {(children == 1 ? "is" : "are")} included.";
        var hidden = windowStyle == 0
            ? " None is visible, as vbHide starts a program hidden."
            : " None is visible yet: a window still being made is not shown, so ask again.";
        if (windows.Count == 0)
        {
            var none = standing != Standing.Running && children == 0
                ? " No process it started is running, so there is no window to read. A launcher that hands off to another "
                  + "program and exits reads this way once that program has exited too."
                : $" No top-level window after {FirstWindowWait.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s: "
                  + "a program shows its first window some time after it starts, so ask again.";
            return new ProcessWindowsResult(true, null, header + self + family + none, started, []);
        }
        return new ProcessWindowsResult(true, null,
            header + self + family + $" {Count(windows.Count, "window")}; " + Units + (noForeground ? NoForeground : "")
            + (windows.Any(w => w.Visible) ? "" : hidden),
            started, windows);
    }

    private static string SelfText(Standing standing) => standing switch
    {
        Standing.Running => "",
        Standing.Reused => " It has exited, and its ID now belongs to a process that started later, which isn't read.",
        _ => " It has exited.",
    };

    // Whether the process Shell started still runs: its ID names a process that started when it did. A later start is
    // another process the ID was reused for.
    private static (Standing Standing, DateTime? HolderStartedUtc) StandingOf(ShellLaunches.Launch launch)
    {
        try
        {
            using var process = Process.GetProcessById(launch.ProcessId);
            var started = process.StartTime.ToUniversalTime();
            if (launch.StartedUtc is not { } recorded)
                return (Standing.Unverifiable, started);
            return (Math.Abs((started - recorded).TotalSeconds) < 1 ? Standing.Running : Standing.Reused, started);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return (Standing.Exited, null);
        }
    }

    private static StartedProcess Listed(ShellLaunches.Launch launch) =>
        new(launch.ProcessId, launch.Command, launch.WindowStyle, StyleName(launch.WindowStyle),
            StandingOf(launch).Standing == Standing.Running);

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string Count(int n, string noun) =>
        $"{n.ToString(CultureInfo.InvariantCulture)} {noun}{(n == 1 ? "" : noun.EndsWith('s') ? "es" : "s")}";
}
