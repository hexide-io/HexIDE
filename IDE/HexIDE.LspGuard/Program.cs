using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HexIDE.LspGuard;

internal static partial class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || args.Length < 2 || args[0] != "--")
        {
            Console.Error.WriteLine("Usage: HexIDE.LspGuard -- <server> <args...> (Linux/macOS)");
            return 1;
        }

        if (SetProcessGroup(0, 0) != 0)
        {
            Console.Error.WriteLine($"Could not create the language server process group: {Marshal.GetLastPInvokeError()}");
            return 1;
        }
        var parent = GetParentProcessId();
        var parentGroup = GetProcessGroup(parent);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Remain alive after the group's SIGTERM so even descendants that ignore it get SIGKILL.
        using var termination = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            stopping.TrySetResult();
        });

        var start = new ProcessStartInfo(args[1]) { UseShellExecute = false };
        foreach (var argument in args.Skip(2)) start.ArgumentList.Add(argument);
        Process server;
        try { server = Process.Start(start)!; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not start language server '{args[1]}': {ex.Message}");
            return 1;
        }
        using (server)
        {
            var exited = server.WaitForExitAsync();
            int? exitCode = null;
            while (!stopping.Task.IsCompleted)
            {
                if (GetParentProcessId() != parent) break;
                if (exited.IsCompleted)
                {
                    exitCode = server.ExitCode;
                    break;
                }
                await Task.WhenAny(exited, stopping.Task, Task.Delay(500));
            }
            var group = Environment.ProcessId;
            // Leave before signalling: an empty server group can then disappear, and a clean
            // exit retains its code even when a descendant needs SIGKILL (#2297, #4152).
            var leftGroup = parentGroup > 0 && SetProcessGroup(0, parentGroup) == 0;
            var cleanup = Stopwatch.StartNew();
            Kill(-group, 15);
            while (cleanup.Elapsed < TimeSpan.FromMilliseconds(500))
            {
                if (Kill(-group, 0) != 0 && Marshal.GetLastPInvokeError() == 3) // ESRCH
                    return exitCode ?? 1;
                await Task.Delay(10);
            }
            Kill(-group, 9);
            if (!leftGroup) return 1; // SIGKILL also ends this guard.
            return exitCode ?? 1;
        }
    }

    [LibraryImport("libc", EntryPoint = "setpgid", SetLastError = true)]
    private static partial int SetProcessGroup(int pid, int group);

    [LibraryImport("libc", EntryPoint = "getppid")]
    private static partial int GetParentProcessId();

    [LibraryImport("libc", EntryPoint = "getpgid")]
    private static partial int GetProcessGroup(int pid);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);
}
