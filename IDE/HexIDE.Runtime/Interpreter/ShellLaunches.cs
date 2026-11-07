using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The processes a running program has started with <c>Shell</c>, so automation can describe their windows (#2591). It
/// records them and nothing more: nothing here waits for them, stops them or reads them.
/// </summary>
/// <remarks>
/// What <c>Shell</c> does lands in another process's window, which no tool reached, so a program's <c>Shell</c> couldn't
/// be checked. Recording each launch is what bounds that reach: a process this list doesn't hold, or a process its ID
/// now names that started later than the launch, is not one the program started, and is not read.
/// </remarks>
public static class ShellLaunches
{
    /// <summary>One process <c>Shell</c> started: the ID it returned, its command line and window style as given, and
    /// when the process started, or null when it had gone before that could be read.</summary>
    public sealed record Launch(int ProcessId, string Command, int WindowStyle, DateTime? StartedUtc);

    // A program that shells in a loop must not grow this without bound; the oldest launches go first.
    internal const int Kept = 100;

    private static readonly Lock Gate = new();
    private static readonly List<Launch> Launches = [];

    /// <summary>Every launch recorded in this process's lifetime, oldest first, at most <see cref="Kept"/>.</summary>
    public static IReadOnlyList<Launch> All
    {
        get { lock (Gate) return Launches.ToArray(); }
    }

    internal static void Record(int processId, string command, int windowStyle)
    {
        DateTime? started = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            started = process.StartTime.ToUniversalTime();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception
                                       or NotSupportedException)
        {
            // Gone already, or not ours to read: the launch is still recorded, with no start time to tell reuse by.
        }
        Record(new Launch(processId, command, windowStyle, started));
    }

    internal static void Record(Launch launch)
    {
        lock (Gate)
        {
            Launches.Add(launch);
            if (Launches.Count > Kept)
                Launches.RemoveAt(0);
        }
    }
}
