using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Lsp;

[assembly: AssemblyFixture(typeof(HexIDE.Integration.Tests.LanguageServerLeakGuard))]

namespace HexIDE.Integration.Tests;

/// <summary>
/// Fails the run if a language server that a test started is still running when the suite ends, and names the test
/// that started it (#2530).
/// </summary>
/// <remarks>
/// A leaked server is more than a stray process. Its client keeps reading on pool threads that post to
/// <c>Dispatcher.UIThread</c>, and one of those can take the UI dispatcher in the moment Avalonia.Headless clears it
/// for the next test. That ends the headless session, and every later test waits forever. Build a real composition
/// through <see cref="RealComposition"/>, which disposes the client.
/// </remarks>
public sealed class LanguageServerLeakGuard : IAsyncDisposable
{
    internal sealed record Launch(int Id, DateTime StartTime, string Command, string Test);

    internal static readonly ConcurrentDictionary<int, Launch> Launched = new();

    public LanguageServerLeakGuard() => LaunchedServerGuard.Started += Note;

    // Called on the launching thread, inside the transport's start. It must not throw.
    internal static void Note(Process process)
    {
        try
        {
            var start = process.StartInfo;
            var arguments = start.ArgumentList.Count > 0 ? string.Join(' ', start.ArgumentList) : start.Arguments;
            Launched[process.Id] = new Launch(process.Id, process.StartTime, $"{start.FileName} {arguments}".Trim(),
                TestContext.Current.Test?.TestDisplayName ?? "outside a test");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Already gone, so there is nothing to leak.
        }
    }

    internal static IReadOnlyList<Launch> StillRunning() => [.. Launched.Values.Where(IsRunning).OrderBy(l => l.StartTime)];

    // The start time tells the server apart from a later process that reused its id.
    private static bool IsRunning(Launch launch)
    {
        try
        {
            using var process = Process.GetProcessById(launch.Id);
            return !process.HasExited && process.StartTime == launch.StartTime;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    internal static void Check()
    {
        var running = StillRunning();
        running.Should().BeEmpty(
            "every test must dispose the language-server client it started, or its server outlives it:{0}{1}",
            Environment.NewLine, string.Join(Environment.NewLine, running.Select(l =>
                $"process {l.Id} ({l.Command}), started by {l.Test}")));
    }

    public ValueTask DisposeAsync()
    {
        LaunchedServerGuard.Started -= Note;
        Check();
        return ValueTask.CompletedTask;
    }
}
