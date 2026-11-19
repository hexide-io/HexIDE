using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Awaits a debugger signal with a wall-clock guard that says what it was waiting for when it fails.
///
/// The nine debugger fixtures each coordinate the same way: subscribe to <c>Stopped</c>, act, await the
/// completion source. That part is sound — no polling, no sleeping, and the subscription is always in
/// place before the action. What is NOT sound is the guard around it, and their own comments say so:
/// <i>"generous — avoid false timeouts under full-suite parallel load"</i>. Fifteen seconds is generous,
/// but it is still wall clock, and a wall-clock assertion can be lost to a busy machine.
///
/// <para>
/// So when one does fail, the output needs to be worth reading. <c>Task.WaitAsync</c> throws a bare
/// <see cref="TimeoutException"/> — no indication of WHICH await expired, which in a test with three of
/// them leaves you guessing. Issue #102 is exactly that: a single recorded failure with no diagnosis,
/// which could not be reproduced in eight subsequent full-suite runs and so could not be fixed either.
/// </para>
///
/// <para>
/// This does not make the wait deterministic — nothing can, short of the product exposing a synchronous
/// step API. It makes the next occurrence <b>evidence</b> instead of a shrug, which is the difference
/// between a flake that gets fixed and one that teaches everybody to re-run red CI.
/// </para>
/// </summary>
internal static class DebuggerSignalGuard
{
    /// <summary>
    /// The wall-clock ceiling on a debugger signal. Generous on purpose: it exists to stop a hung test
    /// wedging the run, not to assert anything about how fast the interpreter is.
    /// </summary>
    internal static readonly TimeSpan Default = TimeSpan.FromSeconds(15);

    /// <summary>How long a signal that missed its guard is waited for again, to tell late from never: as long
    /// as the guard itself, up to this.</summary>
    internal static readonly TimeSpan LateCap = TimeSpan.FromSeconds(30);

    /// <param name="state">What the test was doing, read the moment the guard runs out: a recursion's depth,
    /// the run's status, the debugger's state. Optional.</param>
    internal static async Task<T> Guarded<T>(
        this Task<T> signal,
        TimeSpan? guard = null,
        Func<string>? state = null,
        [CallerArgumentExpression(nameof(signal))] string? awaited = null,
        [CallerMemberName] string? test = null,
        [CallerLineNumber] int line = 0)
    {
        await Wait(signal, guard ?? Default, state, awaited, test, line);
        return await signal;
    }

    /// <inheritdoc cref="Guarded{T}"/>
    internal static Task Guarded(
        this Task signal,
        TimeSpan? guard = null,
        Func<string>? state = null,
        [CallerArgumentExpression(nameof(signal))] string? awaited = null,
        [CallerMemberName] string? test = null,
        [CallerLineNumber] int line = 0) =>
        Wait(signal, guard ?? Default, state, awaited, test, line);

    private static async Task Wait(Task signal, TimeSpan budget, Func<string>? state, string? awaited, string? test, int line)
    {
        try
        {
            await signal.WaitAsync(budget);
            return;
        }
        catch (TimeoutException) when (!signal.IsCompleted)
        {
        }
        // Read at the moment the guard ran out, before waiting on: the runner's load over the guard's window,
        // and what the test says it was doing. The window starts 5 s earlier, so a process stopped for the
        // whole guard still has the sample from before it stopped, and the sampler gap measures the stop.
        var load = RunnerLoad.Describe(budget + TimeSpan.FromSeconds(5));
        var doing = state?.Invoke();
        var late = budget < LateCap ? budget : LateCap;
        var since = Stopwatch.StartNew();
        var arrived = await Task.WhenAny(signal, Task.Delay(late, TestContext.Current.CancellationToken)) == signal;
        var after = arrived
            ? $"It arrived {since.ElapsedMilliseconds:N0} ms after the guard ran out, so it came late."
            : $"It had still not arrived {late.TotalSeconds:0.###} s after that, so it may never come.";
        throw new TimeoutException(Explain(awaited, test, line, budget, after, doing, load));
    }

    private static string Explain(string? awaited, string? test, int line, TimeSpan budget, string after,
        string? doing, string load)
    {
        List<string> lines =
        [
            $"The debugger signal `{awaited}` did not arrive within {budget.TotalSeconds:0.###}s ({test}, line {line}). {after}",
            char.ToUpperInvariant(load[0]) + load[1..] + ".",
            "",
            "Which of the two it is decides what it is:",
            "  - a signal that never came: the debugger did not break where the test expected, or the run",
            "    stopped. That is a real defect, and the state above says where it was.",
            "  - a signal that came late: look at the runner's load. A worst dispatch in seconds is a starved",
            "    thread pool; a sampler gap in seconds is the whole process stopped, as a paused VM or a",
            "    long GC does.",
            "",
            "A single green run is not evidence about an intermittent failure; that mistake was made twice",
            "while fixing #93. Record this occurrence, with this message, on the test's issue labelled",
            "`flaky`, or open one.",
        ];
        if (doing is not null)
            lines.Insert(1, $"When the guard ran out: {doing}.");
        return string.Join(Environment.NewLine, lines);
    }
}
