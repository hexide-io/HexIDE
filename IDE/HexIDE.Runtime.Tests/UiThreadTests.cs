using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The thread the IDE and a made EXE start their UI on, which is where VB code runs (#3228).
/// </summary>
public class UiThreadTests : BaseVBTestFixture
{
    [Fact]
    public void Run_GivesTheUiAThreadOfItsOwn_WithTheSizedStack()
    {
        var caller = Environment.CurrentManagedThreadId;
        var (thread, reserved, apartment) = (0, 0UL, ApartmentState.Unknown);

        var result = UiThread.Run(() =>
        {
            thread = Environment.CurrentManagedThreadId;
            reserved = OperatingSystem.IsMacOS() ? 0 : ReservedStackBytes();
            apartment = OperatingSystem.IsWindows() ? Thread.CurrentThread.GetApartmentState() : ApartmentState.Unknown;
            return 42;
        });

        result.Should().Be(42);
        if (OperatingSystem.IsMacOS())
        {
            // AppKit runs its UI on the process's first thread only, so macOS stays there.
            thread.Should().Be(caller);
            return;
        }
        thread.Should().NotBe(caller);
        reserved.Should().Be((ulong)UiThread.StackBytes);
        if (OperatingSystem.IsWindows())
            apartment.Should().Be(ApartmentState.STA, "OLE drag-and-drop, the clipboard and OCX hosting need it");
    }

    [Fact]
    public void Run_RethrowsWhatTheUiThreadThrows()
    {
        var run = () => UiThread.Run(() => throw new InvalidOperationException("thrown on the UI thread"));

        run.Should().Throw<InvalidOperationException>().WithMessage("thrown on the UI thread");
    }

    [Fact]
    public async Task DeepRecursion_RunsToTheEnd_OnTheUiThreadsStack()
    {
        // About 1,550 levels fit in a Debug build on Windows, where its first thread holds 31 (#3228). The floor
        // is under three quarters of that, so frames can grow somewhat before this fails, as in StackSpaceTests.
        await RunOnUiThreadStack(
            "Debug.Print Down(1100)\n" +
            "Function Down(n)\n" +
            "  If n <= 0 Then\n" +
            "    Down = 0\n" +
            "  Else\n" +
            "    Down = Down(n - 1) + 1\n" +
            "  End If\n" +
            "End Function\n");
        AssertDebugLog([1100]);
    }

    [Fact]
    public async Task UnboundedRecursion_StillRaisesError28_OnTheUiThreadsStack()
    {
        await RunOnUiThreadStack(
            "On Error Resume Next\n" +
            "Call Up(0)\n" +
            "Debug.Print Err.Number\n" +
            "Function Up(n)\n" +
            "  Up = Up(n + 1)\n" +
            "End Function\n");
        AssertDebugLog([new Vb6Value(28L)]);
    }

    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "The launcher's stack limit is a Linux question", SkipUnless = nameof(IsLinux))]
    public async Task OnLinux_TheUiThreadsStack_IsTheSameWhateverUlimitSays()
    {
        // Under `ulimit -s unlimited` the first thread reports terabytes, and the Error 28 guard never trips (#3228).
        // Each run is a child process, because a process's own stack limit is fixed when it starts.
        foreach (var limit in new[] { "8192", "unlimited" })
            (await ReservedInChildUnder(limit)).Should().Be(UiThread.StackBytes.ToString(CultureInfo.InvariantCulture),
                $"the UI thread's stack must not follow ulimit -s {limit}");
    }

    private const string ChildReport = "HEXIDE_UITHREAD_CHILD_REPORT";

    public static bool IsChild => Environment.GetEnvironmentVariable(ChildReport) is { Length: > 0 };

    [Fact(Skip = "Runs only as the child of OnLinux_TheUiThreadsStack_IsTheSameWhateverUlimitSays", SkipUnless = nameof(IsChild))]
    public void Child_ReportsTheUiThreadsStack()
    {
        var reserved = UiThread.Run(() => (int)ReservedStackBytes());
        File.WriteAllText(Environment.GetEnvironmentVariable(ChildReport)!, reserved.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Runs this test assembly again, filtered to <see cref="Child_ReportsTheUiThreadsStack"/>, under
    /// <c>ulimit -s <paramref name="limit"/></c>, and returns what it reports. Nothing recurses in the child, and it
    /// has two minutes.</summary>
    private static async Task<string> ReservedInChildUnder(string limit)
    {
        var report = Path.Combine(Path.GetTempPath(), $"hexide-uithread-{Guid.NewGuid():N}.txt");
        var host = Environment.ProcessPath!;
        var start = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // $1 is the limit and the rest is the command, so nothing is quoted into the script.
        foreach (var argument in new[] { "-c", "ulimit -s \"$1\" || exit 97; shift; exec \"$@\"", "sh", limit })
            start.ArgumentList.Add(argument);
        if (Path.GetFileNameWithoutExtension(host) == "dotnet")
            start.ArgumentList.Add(host);
        start.ArgumentList.Add(Path.GetFileNameWithoutExtension(host) == "dotnet" ? typeof(UiThreadTests).Assembly.Location : host);
        // Started directly, the test app is xunit's own runner, which filters with -method; --filter-method is
        // the dotnet test runner's.
        start.ArgumentList.Add("-method");
        start.ArgumentList.Add("*" + nameof(Child_ReportsTheUiThreadsStack));
        start.Environment[ChildReport] = report;
        try
        {
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var errors = child.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try
            {
                await child.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                child.Kill(entireProcessTree: true);
                throw new TimeoutException($"The child under ulimit -s {limit} didn't finish in two minutes.");
            }
            File.Exists(report).Should().BeTrue(
                $"the child under ulimit -s {limit} should report; it exited {child.ExitCode}:\n{await output}\n{await errors}");
            return File.ReadAllText(report);
        }
        finally
        {
            File.Delete(report);
        }
    }

    /// <summary>Runs <paramref name="code"/> on a thread sized as <see cref="UiThread"/> sizes the UI's, on every
    /// platform. A run with no debugger attached never yields, so all of its recursion is on that stack.</summary>
    private async Task RunOnUiThreadStack(string code)
    {
        Exception? failure = null;
        await Task.Run(() => UiThread.RunOnNewThread(() =>
        {
            try
            {
                var run = Run(code);
                // After a yield the rest of the run would go on a pool thread, whose stack isn't the one under test.
                if (!run.IsCompleted)
                    throw new InvalidOperationException("The run yielded, so its recursion left the thread under test.");
                run.GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                failure = error;
            }
            return 0;
        }, UiThread.StackBytes), TestContext.Current.CancellationToken);
        if (failure is not null)
            throw failure;
    }

    /// <summary>The whole reserved stack of the calling thread, guard pages included, on Windows or Linux.</summary>
    private static ulong ReservedStackBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            GetCurrentThreadStackLimits(out var low, out var high);
            return high - low;
        }
        var attr = new byte[256];
        pthread_getattr_np(pthread_self(), attr).Should().Be(0);
        pthread_attr_getstack(attr, out _, out var size).Should().Be(0);
        pthread_attr_destroy(attr);
        return size;
    }

    [DllImport("kernel32")] private static extern void GetCurrentThreadStackLimits(out nuint low, out nuint high);
    [DllImport("libc")] private static extern nint pthread_self();
    [DllImport("libc")] private static extern int pthread_getattr_np(nint thread, [In, Out] byte[] attr);
    [DllImport("libc")] private static extern int pthread_attr_getstack([In, Out] byte[] attr, out nint address, out nuint size);
    [DllImport("libc")] private static extern int pthread_attr_destroy([In, Out] byte[] attr);
}
