using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HexIDE.Lsp;

internal static partial class LaunchedServerGuard
{
    // Unix guards need two more seconds to end the server's remaining group after it exits.
    public static TimeSpan ExitTimeout => TimeSpan.FromSeconds(
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? 4 : 2);

    public static void Wrap(ProcessStartInfo start)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())) return;

        var server = start.FileName;
        // Preserve immediate launch failures (and their diagnostics) rather than reporting a guard
        // that started successfully for a server that cannot be executed.
        var candidates = server.Contains('/')
            ? new[] { Path.GetFullPath(server) }
            : new[] { Path.Join(AppContext.BaseDirectory, server) }
                .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(':')
                    .Select(directory => Path.GetFullPath(Path.Join(directory, server))));
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null) throw new Win32Exception(2, $"Language server '{server}' was not found.");
        if (Access(executable, 1) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Language server '{server}' is not executable.");
        server = executable;
        var guard = Path.Join(AppContext.BaseDirectory, "HexIDE.LspGuard");
        var prefix = new List<string>();
        const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        if (File.Exists(guard) && (File.GetUnixFileMode(guard) & execute) != 0)
            start.FileName = guard;
        else
        {
            start.FileName = "dotnet";
            prefix.Add(guard + ".dll");
        }
        prefix.Add("--");
        prefix.Add(server);
        if (start.ArgumentList.Count > 0)
        {
            for (var i = 0; i < prefix.Count; i++) start.ArgumentList.Insert(i, prefix[i]);
        }
        else
            start.Arguments = string.Join(' ', prefix.Select(Quote)) + " " + start.Arguments;
    }

    public static void Kill(Process process)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            // The protocol grace period has elapsed: end the guard and its group together.
            // Before setpgid completes there is no such group; fall back to the process tree then.
            if (SendSignal(-process.Id, 9) == 0) return;
        }
        process.Kill(entireProcessTree: true);
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    [LibraryImport("libc", EntryPoint = "access", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Access(string path, int mode);

    [LibraryImport("libc", EntryPoint = "kill")]
    private static partial int SendSignal(int pid, int signal);
}
