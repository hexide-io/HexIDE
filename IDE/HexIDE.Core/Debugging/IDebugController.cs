using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Debugging;

/// <summary>
/// The DAP-shaped seam between a running program and any debugger front-end (the IDE debug UI, the MCP debug tools,
/// or a future external DAP client). It models DAP <em>concepts</em> — breakpoints, a "stopped" event carrying a
/// reason + location, continue/step commands, and scopes/variables/evaluate — <em>without</em> the DAP wire protocol.
///
/// This is the half a front end uses: it sets breakpoints and issues Continue/Pause/Step/Stop. It names nothing of
/// any one engine's. What the interpreter itself calls, at every statement, is <c>IDebugGate</c> in
/// <c>HexIDE.Runtime</c>, which extends this.
/// </summary>
public interface IDebugController
{
    /// <summary>Running (walking), Paused (in Break mode), or Stopped (reset/torn down).</summary>
    DebugState State { get; }

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

    /// <summary>Replace per-line expressions after pushing the module's breakpoint line set.</summary>
    void SetBreakpointConditions(string module, IReadOnlyDictionary<int, string> conditions);

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

    /// <summary>Reset: unwind the running walk with no Class_Terminate,
    /// matching VB6 <c>End</c>. Idempotent.</summary>
    void Stop();

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

    /// <summary>Evaluate a stored watch in its module or procedure context while paused.</summary>
    Task<DebugEvalResult?> EvaluateScopedWatchAsync(string expression, string context)
        => EvaluateWatchAsync(expression);

    /// <summary>The Call Stack (deepest/current frame first), or empty when not in Break mode — read by the Call
    /// Stack window and the MCP <c>get_call_stack</c> tool on each <see cref="Stopped"/>. DAP <c>stackTrace</c>-shaped.</summary>
    IReadOnlyList<CallStackFrame> GetCallStack();
}

/// <summary>The debugger's execution state.</summary>
public enum DebugState { Running, Paused, Stopped }

/// <summary>Why the walk entered Break mode.</summary>
public enum StopReason { Breakpoint, Step, Pause, StopStatement, Watch, RuntimeError, Assertion }

/// <summary>A break-type watch pushed to the gate: the <paramref name="Expression"/> to evaluate, and whether it
/// breaks on CHANGE (Break When Value Has Changed) rather than on TRUE (Break When Value Is True).</summary>
public readonly record struct WatchBreakSpec(string Expression, bool BreakOnChange);

/// <summary>Where and why execution is paused (line is 1-based, matching the editor and ANTLR).</summary>
public readonly record struct StoppedInfo(StopReason Reason, string Module, int Line,
    string? ConditionExpression = null, string? ConditionError = null);
