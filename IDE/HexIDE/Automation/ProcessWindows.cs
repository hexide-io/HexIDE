using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HexIDE.Automation;

/// <summary>
/// A top-level window of another process: its owner's ID, title, Normal/Minimized/Maximized/Hidden state, whether it is
/// visible, its bounds in screen pixels as [left, top, width, height], whether it is the foreground window, and its
/// window class (#2591).
/// </summary>
public sealed record ProcessWindow(int ProcessId, string Title, string State, bool Visible, int[] Bounds, bool IsForeground,
    string ClassName);

/// <summary>
/// Reads the top-level windows of other processes, and the processes one started in turn, on Windows (#2591). Read-only:
/// it never activates, moves or sends anything to a window. Which processes may be read is the caller's to decide.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ProcessWindows
{
    /// <summary>
    /// The processes <paramref name="root"/> started, and those they started, to any depth. A child is one whose parent ID
    /// is its parent's and which started no earlier than its parent did, so a reused ID is not taken for a child. A root
    /// that has exited still has its children found, by the start time it was recorded with. When the root's ID has
    /// since passed to another process, <paramref name="reusedSinceUtc"/> is when that one started: the root's own
    /// children started before it, and the new holder's after.
    /// </summary>
    public static IReadOnlyList<int> Descendants(int root, DateTime? rootStartedUtc, DateTime? reusedSinceUtc = null)
    {
        var entries = Snapshot();
        var found = new List<int>();
        var pending = new Queue<(int Id, DateTime? Started)>([(root, rootStartedUtc)]);
        while (pending.Count > 0)
        {
            var (parent, parentStarted) = pending.Dequeue();
            foreach (var (id, parentId) in entries)
            {
                if (parentId != parent || id == parent || found.Contains(id) || id == root)
                    continue;
                var started = StartedUtc(id);
                if (started is null || parentStarted is { } after && started < after
                    || parent == root && reusedSinceUtc is { } reused && started >= reused)
                    continue;
                found.Add(id);
                pending.Enqueue((id, started));
            }
        }
        return found;
    }

    /// <summary>The top-level windows of <paramref name="processIds"/> a person could see or bring back: every visible
    /// one, and hidden ones that have a title bar, as a window started hidden does. Helper windows, which have neither,
    /// are left out. <paramref name="noForeground"/> is true when this session has no foreground window at all.</summary>
    public static IReadOnlyList<ProcessWindow> Of(IReadOnlyCollection<int> processIds, out bool noForeground)
    {
        var foreground = GetForegroundWindow();
        noForeground = foreground == 0;
        var windows = new List<ProcessWindow>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (!processIds.Contains((int)owner))
                return true;
            var visible = IsWindowVisible(hwnd);
            if (!visible && (GetWindowLong(hwnd, GwlStyle) & WsCaption) != WsCaption)
                return true;
            GetWindowRect(hwnd, out var rect);
            // Hidden first: a hidden window's show state reads Normal, which a caller took for a window on screen.
            var state = !visible ? "Hidden" : IsIconic(hwnd) ? "Minimized" : IsZoomed(hwnd) ? "Maximized" : "Normal";
            windows.Add(new ProcessWindow((int)owner, Text(hwnd), state, visible,
                [rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top], hwnd == foreground, ClassName(hwnd)));
            return true;
        }, 0);
        return windows;
    }

    private static DateTime? StartedUtc(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    // Every process's ID and its parent's, from one snapshot.
    private static List<(int Id, int ParentId)> Snapshot()
    {
        var entries = new List<(int, int)>();
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1)
            return entries;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var more = Process32First(snapshot, ref entry); more; more = Process32Next(snapshot, ref entry))
                entries.Add(((int)entry.ProcessId, (int)entry.ParentProcessId));
        }
        finally { CloseHandle(snapshot); }
        return entries;
    }

    private static string Text(nint hwnd)
    {
        var buffer = new char[512];
        var length = GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, length));
    }

    private static string ClassName(nint hwnd)
    {
        var buffer = new char[256];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(0, length));
    }

    private const int GwlStyle = -16;
    private const uint WsCaption = 0x00C00000;
    private const uint SnapProcess = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint parameter);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern uint GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, char[] text, int count);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, char[] text, int count);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
