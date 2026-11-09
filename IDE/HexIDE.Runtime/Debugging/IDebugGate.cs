using System;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Debugging;

/// <summary>
/// The half of the debugger the interpreter calls. It awaits <see cref="OnStatementAsync"/> at every statement (the
/// cooperative pause-gate), breaks for <c>Stop</c> and <c>Debug.Assert</c>, and halts on an unhandled run-time error.
/// The front-end half is <see cref="IDebugController"/>, which this extends so that one object serves both.
///
/// This is runtime <b>execution machinery</b> (it suspends and inspects a live walk) — it does no static
/// analysis, and so is in-bounds under the CST-not-AST limit.
/// </summary>
public interface IDebugGate : IDebugController
{
    /// <summary>Whether this host offers debugging, rather than only a fatal-error halt.</summary>
    bool CanBreak => true;

    /// <summary>True from the moment a reset (<see cref="Stop"/>) begins until the walk finishes unwinding. While
    /// set, the gate raises <see cref="StopExecutionSignal"/> and the interpreter suppresses <c>Class_Terminate</c>
    /// — VB6 <c>End</c> abruptly terminates and fires NO Terminate (oracle-verified).</summary>
    bool IsAborting { get; }

    /// <summary>The run's generation, which <see cref="Reset"/> advances. Code of a run that has ended can resume
    /// after the next run has begun, by which time <see cref="IsAborting"/> has been cleared again; a generation
    /// taken while the run was live tells it the run it belongs to has gone.</summary>
    int Session { get; }

    /// <summary>Called by the interpreter when the outermost activation unwinds — a dispatch (event-handler chain)
    /// has returned to idle. Disarms any step that was armed but never consumed, so a leftover Step Into/Over/Out
    /// can't spuriously break the NEXT, unrelated event. A no-op unless a step is armed and the state is Running.</summary>
    void NotifyDispatchIdle();

    /// <summary>The cooperative pause-gate — the interpreter awaits this before executing each statement. Fast,
    /// synchronous, and allocation-free on the hot path (no breakpoint here, not stepping, not paused); it only
    /// goes async to suspend at a break, to hold a frozen newcomer, or to time-slice a yield so a Ctrl+Break can
    /// land on a tight loop. <paramref name="line"/> is the statement's 1-based source line; <paramref
    /// name="module"/> is the runtime module name (matched against the breakpoint set); <paramref name="frame"/> is
    /// the executing activation (captured for Locals only when this statement decides to break — no hot-path cost).</summary>
    ValueTask OnStatementAsync(int line, string module, IDebugFrame? frame);

    /// <summary>A breakpoint-only position leaves stepping at its existing statement positions.</summary>
    ValueTask OnBreakpointPositionAsync(int line, string module, IDebugFrame frame) => ValueTask.CompletedTask;

    /// <summary>Enter Break for the <c>Stop</c> statement (reason = StopStatement). Awaited by the interpreter so
    /// execution suspends exactly as it would at a breakpoint.</summary>
    ValueTask EnterBreakFromStopStatementAsync(int line, string module, IDebugFrame? frame);

    /// <summary>Enter Break for a <c>Debug.Assert</c> whose condition does not hold (reason = Assertion), as for
    /// <see cref="EnterBreakFromStopStatementAsync"/>.</summary>
    ValueTask EnterBreakFromAssertionAsync(int line, string module, IDebugFrame? frame);

    /// <summary>
    /// Who answers a run-time error that nothing handles (#1011): the host, which shows VB6's dialog. It answers true for
    /// Debug, and false for End once it has ended the run. A standalone host offers only OK and exits. While it is null
    /// the program does not halt: the error unwinds to the event's dispatch, which reports it, as before.
    /// </summary>
    Func<VBRunTimeException, Task<bool>>? HaltHost { get; set; }

    /// <summary>Whether a run-time error nothing handles halts the program: whether <see cref="HaltHost"/> is set.</summary>
    bool CanHalt { get; }

    /// <summary>
    /// Halt on a run-time error nothing handles, at the statement that raised it, before anything unwinds (#1011). The
    /// interpreter awaits this inside the faulting statement. It returns true once the developer chose Debug and the break
    /// that followed has been resumed, for the statement to run again, all of it, as VB6 retries it (measured). End raises
    /// <see cref="StopExecutionSignal"/>, as the End button does. False means no host is set, and the error goes on.
    /// </summary>
    ValueTask<bool> HaltAsync(VBRunTimeException error, int line, string module, IDebugFrame? frame);
}

/// <summary>
/// Internal control-flow exception that unwinds the interpreter's walk on a debugger reset (Stop). Like the
/// interpreter's <c>GoToSignal</c>/<c>ResumeSignal</c> it is NOT a <c>VBRunTimeException</c>, so the On-Error
/// traps never catch it; it propagates through every <c>try/finally</c> (refcount teardown runs but is suppressed
/// while <see cref="IDebugGate.IsAborting"/> is set) up to the interpreter's entry point, which swallows it.
/// Public so the run-window boundary can defensively catch it too.
/// </summary>
public sealed class StopExecutionSignal : Exception
{
    public StopExecutionSignal() : base("Debugger reset (Stop)") { }
}

/// <summary>Raised inside an Immediate-window evaluation for an operation that isn't supported there (v1: calling a
/// user Sub/Function, which would also deadlock the paused UI-thread gate). The evaluator catches it and returns
/// the message as the printed result — it is NOT a program runtime error.</summary>
public sealed class ImmediateEvalException : Exception
{
    public ImmediateEvalException(string message) : base(message) { }
}
