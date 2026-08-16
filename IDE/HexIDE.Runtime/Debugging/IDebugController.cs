using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Debugging;

/// <summary>
/// The DAP-shaped seam between the running interpreter and any debugger front-end (the IDE debug UI, the MCP
/// debug tools, or a future external DAP client). It models DAP <em>concepts</em> — breakpoints, a "stopped"
/// event carrying a reason + location, continue/step commands, and (later phases) scopes/variables/evaluate —
/// <em>without</em> the DAP wire protocol. The interpreter calls <see cref="OnStatementAsync"/> at every statement
/// (the cooperative pause-gate); the front-end sets breakpoints and issues Continue/Pause/Step/Stop.
///
/// This is runtime <b>execution machinery</b> (it suspends and inspects a live walk) — it does no static
/// analysis, and so is in-bounds under the CST-not-AST limit.
/// </summary>
public interface IDebugController
{
    /// <summary>Running (walking), Paused (in Break mode), or Stopped (reset/torn down).</summary>
    DebugState State { get; }

    /// <summary>True from the moment a reset (<see cref="Stop"/>) begins until the walk finishes unwinding. While
    /// set, the gate raises <see cref="StopExecutionSignal"/> and the interpreter suppresses <c>Class_Terminate</c>
    /// — VB6 <c>End</c> abruptly terminates and fires NO Terminate (oracle-verified).</summary>
    bool IsAborting { get; }

    /// <summary>True from a run's <see cref="Reset"/> (run start) until its <see cref="Stop"/> (run end) — i.e.
    /// while the project is running OR paused in the debugger. Lets the IDE know a run is live without depending on
    /// the project runner (which would cycle through the editor factory).</summary>
    bool IsSessionActive { get; }

    /// <summary>Where the walk is paused (module + 1-based line + reason), or null when not paused.</summary>
    StoppedInfo? CurrentStop { get; }

    /// <summary>Fired (synchronously, on the walk's thread) when execution enters Break mode.</summary>
    event Action<StoppedInfo>? Stopped;

    /// <summary>Fired when execution leaves Break mode — resumed (Continue/Step) OR ended (<see cref="Stop"/>).
    /// Front-ends clear the current-statement indicator and re-query run commands on this.</summary>
    event Action? Continued;

    /// <summary>Prepare for a fresh run: back to Running, clearing paused/aborting state. Breakpoints persist. The
    /// IDE calls this at the start of every run (a freshly-constructed controller already starts reset).</summary>
    void Reset();

    /// <summary>Replace the breakpoint line set for a module (1-based lines). The front-end pushes the current
    /// set; the gate matches against it. An empty set clears the module's breakpoints.</summary>
    void SetBreakpoints(string module, IReadOnlyCollection<int> lines);

    /// <summary>Drop every module's breakpoints. The front-end calls this before re-applying at run start so a
    /// document cleared between runs doesn't leave a stale set (breakpoints survive <see cref="Reset"/>).</summary>
    void ClearBreakpoints();

    /// <summary>Replace the set of break-type watches (VB6 "Break When Value Is True" / "Break When Value Has
    /// Changed"). Each is evaluated against the executing frame at EVERY statement while running — expensive, so the
    /// front-end passes ONLY the break-type watches (Watch-Expression display watches are not included); an empty
    /// list clears them. Break-When-True breaks whenever the expression coerces to True; Break-When-Changed breaks
    /// when its displayed value differs from the previous statement's. Last-seen values are preserved across a
    /// re-push for expressions still present, so editing an unrelated watch doesn't reset a Break-When-Changed
    /// baseline.</summary>
    void SetWatchBreaks(IReadOnlyList<WatchBreakSpec> watches);

    /// <summary>Arm a one-shot "run to here" target — a temporary breakpoint at (<paramref name="module"/>, 1-based
    /// <paramref name="line"/>) that the gate breaks on ONCE, then clears. Run To Cursor: the front-end arms this,
    /// then Continues (if paused) or starts the run (if idle). Survives intermediate breaks (a real breakpoint hit
    /// first stays paused there; Continue proceeds toward the target) but is cleared by <see cref="Reset"/> so a
    /// fresh run carries no stale target. If the line is never reached, the program simply runs on.</summary>
    void RunToCursor(string module, int line);

    /// <summary>Resume from Break mode and run until the next breakpoint / pause / end.</summary>
    void Continue();

    /// <summary>Request a pause (Ctrl+Break). The next statement gate breaks; a no-op if the program is idle.</summary>
    void Pause();

    /// <summary>Step Into (F8): arm one-shot step mode so the next executed statement — this frame or a callee's
    /// first statement (it descends into calls) — breaks with reason=Step. If Paused, resumes the current frame
    /// (leaving frozen events frozen); if Idle/Running, just arms (the starting run's first statement, or the
    /// running walk's next one, breaks).</summary>
    void StepInto();

    /// <summary>Step Over (Shift+F8): run any called procedure to completion, breaking at the next statement in the
    /// current frame (or shallower). Behaves like Step Into on a non-call statement, or when invoked from idle.</summary>
    void StepOver();

    /// <summary>Step Out (Ctrl+Shift+F8): run until the current procedure returns, then break in its caller.</summary>
    void StepOut();

    /// <summary>Set Next Statement (Ctrl+F9): move the execution point to (<paramref name="module"/>, 1-based
    /// <paramref name="line"/>) without running the statements in between — the next Step/Continue executes from
    /// there. TOP-LEVEL-body granularity only, within the CURRENTLY paused procedure. Returns false (refused, no-op)
    /// when not paused, the module isn't the paused frame's, or the target isn't a top-level statement of the paused
    /// procedure (nested inside a block, or the frame is paused inside a nested block). On success the amber
    /// current-statement bar moves to the target.</summary>
    bool SetNextStatement(string module, int line);

    /// <summary>Reset: unwind the running walk (via <see cref="StopExecutionSignal"/>) with no Class_Terminate,
    /// matching VB6 <c>End</c>. Idempotent.</summary>
    void Stop();

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

    /// <summary>Enter Break for the <c>Stop</c> statement (reason = StopStatement). Awaited by the interpreter so
    /// execution suspends exactly as it would at a breakpoint.</summary>
    ValueTask EnterBreakFromStopStatementAsync(int line, string module, IDebugFrame? frame);

    /// <summary>The Locals for the frame that is currently paused, or null when not in Break mode. Read by the IDE
    /// Locals pane and the MCP <c>get_locals</c> tool on each <see cref="Stopped"/>. Children are lazy per node.</summary>
    DebugScope? GetLocals();

    /// <summary>Evaluate an Immediate-window expression against the paused frame (read-only: variables, operators,
    /// intrinsics; user-procedure calls + assignment are rejected). Returns the formatted result or a VB6-style
    /// error message; null when not in Break mode. The IDE strips a leading <c>?</c>/<c>Print</c> before calling.</summary>
    Task<string?> EvaluateAsync(string expression);

    /// <summary>Typed evaluation of a Watch expression against the paused frame — display / type / VB6 truthiness /
    /// an expandable node (or <c>Ok = false</c> with the error message); null when not in Break mode. Read by the
    /// Watches window on each <see cref="Stopped"/>, and (P6b) at the gate for Break-When-True/Changed watches.</summary>
    Task<DebugEvalResult?> EvaluateWatchAsync(string expression);

    /// <summary>The Call Stack (deepest/current frame first), or empty when not in Break mode — read by the Call
    /// Stack window and the MCP <c>get_call_stack</c> tool on each <see cref="Stopped"/>. DAP <c>stackTrace</c>-shaped.</summary>
    IReadOnlyList<CallStackFrame> GetCallStack();
}

/// <summary>The debugger's execution state.</summary>
public enum DebugState { Running, Paused, Stopped }

/// <summary>Why the walk entered Break mode.</summary>
public enum StopReason { Breakpoint, Step, Pause, StopStatement, Watch }

/// <summary>A break-type watch pushed to the gate: the <paramref name="Expression"/> to evaluate, and whether it
/// breaks on CHANGE (Break When Value Has Changed) rather than on TRUE (Break When Value Is True).</summary>
public readonly record struct WatchBreakSpec(string Expression, bool BreakOnChange);

/// <summary>Where and why execution is paused (line is 1-based, matching the editor and ANTLR).</summary>
public readonly record struct StoppedInfo(StopReason Reason, string Module, int Line);

/// <summary>
/// Internal control-flow exception that unwinds the interpreter's walk on a debugger reset (Stop). Like the
/// interpreter's <c>GoToSignal</c>/<c>ResumeSignal</c> it is NOT a <c>VBRunTimeException</c>, so the On-Error
/// traps never catch it; it propagates through every <c>try/finally</c> (refcount teardown runs but is suppressed
/// while <see cref="IDebugController.IsAborting"/> is set) up to the interpreter's entry point, which swallows it.
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
