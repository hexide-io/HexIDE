using System;
using System.Diagnostics;
using System.IO;
using HexIDE.Processes;

namespace HexIDE.Tests.Processes;

public class ChildProcessJobTests
{
    public static bool OnWindows => OperatingSystem.IsWindows();

    // A child that would run for a minute if nothing ended it.
    private static Process StartLongRunningChild() => Process.Start(new ProcessStartInfo(
        Path.Join(Environment.SystemDirectory, "PING.EXE"), "-n 60 127.0.0.1")
    {
        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
    })!;

    [Fact(Skip = "Job objects exist only on Windows.", SkipUnless = nameof(OnWindows))]
    public void Closing_the_job_ends_the_process_put_in_it()
    {
        using var child = StartLongRunningChild();
        try
        {
            var job = new ChildProcessJob();
            job.Assign(child).Should().BeTrue();
            child.HasExited.Should().BeFalse("the child runs until something ends it");

            job.Dispose();

            child.WaitForExit(TimeSpan.FromSeconds(10)).Should().BeTrue("closing the job ends what it holds");
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    [Fact(Skip = "Job objects exist only on Windows.", SkipUnless = nameof(OnWindows))]
    public void A_process_left_out_of_the_job_outlives_it()
    {
        using var child = StartLongRunningChild();
        try
        {
            new ChildProcessJob().Dispose();
            child.WaitForExit(TimeSpan.FromSeconds(2)).Should().BeFalse("only an assigned process ends with the job");
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    [Fact(Skip = "Job objects exist only on Windows.", SkipUnless = nameof(OnWindows))]
    public void A_closed_job_refuses_a_process()
    {
        using var child = StartLongRunningChild();
        try
        {
            var job = new ChildProcessJob();
            job.Dispose();
            job.Invoking(j => j.Assign(child)).Should().Throw<ObjectDisposedException>();
        }
        finally { if (!child.HasExited) child.Kill(); }
    }

    [Fact]
    public void Off_Windows_a_process_is_not_put_in_a_job_and_says_so()
    {
        if (OperatingSystem.IsWindows()) return; // Windows is the three tests above.
        using var child = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false })!;
        try
        {
            using var job = new ChildProcessJob();
            job.Assign(child).Should().BeFalse("there are no job objects here, and the caller keeps its own lifetime handling");
        }
        finally { child.Kill(); }
    }
}
