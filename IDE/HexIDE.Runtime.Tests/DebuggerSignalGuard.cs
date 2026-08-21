using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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

    internal static async Task<T> Guarded<T>(
        this Task<T> signal,
        TimeSpan? guard = null,
        [CallerArgumentExpression(nameof(signal))] string? awaited = null,
        [CallerMemberName] string? test = null,
        [CallerLineNumber] int line = 0)
    {
        var budget = guard ?? Default;
        try
        {
            return await signal.WaitAsync(budget);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(Explain(awaited, test, line, budget));
        }
    }

    internal static async Task Guarded(
        this Task signal,
        TimeSpan? guard = null,
        [CallerArgumentExpression(nameof(signal))] string? awaited = null,
        [CallerMemberName] string? test = null,
        [CallerLineNumber] int line = 0)
    {
        var budget = guard ?? Default;
        try
        {
            await signal.WaitAsync(budget);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(Explain(awaited, test, line, budget));
        }
    }

    private static string Explain(string? awaited, string? test, int line, TimeSpan budget) =>
        $"The debugger signal `{awaited}` did not arrive within {budget.TotalSeconds:0.#}s "
      + $"({test}, line {line}).\n\n"
      + "Two things this distinguishes, which a bare TimeoutException does not:\n"
      + "  - a signal that never came — the debugger did not break where the test expected, which is a\n"
      + "    real defect and reproducible on its own;\n"
      + "  - a signal that came too late — the machine was loaded, which is a flake and will pass on a\n"
      + "    re-run.\n\n"
      + "If this is the FIRST time you have seen it: re-run the whole suite a dozen times before "
      + "concluding anything. A single green run is not evidence about an intermittent failure — that "
      + "mistake was made twice while fixing #93.\n"
      + "Record the occurrence on #102 either way; it has been seen once and never since.";
}
