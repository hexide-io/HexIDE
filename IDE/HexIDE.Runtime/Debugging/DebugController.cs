using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Debugging;

/// <summary>
/// The default <see cref="IDebugController"/> — a cooperative, single-threaded (UI-thread) debug session for the
/// tree-walking interpreter. Everything runs on one thread, so no locking is needed: the walk and the front-end
/// (Continue/Pause/Stop) never run concurrently — the walk is suspended at a <c>TaskCompletionSource</c>
/// whenever the front-end acts.
///
/// <para><b>That invariant is the HOST's to provide, and only the IDE actually provides it.</b> In the IDE every
/// gate continuation posts back to the Avalonia dispatcher, so it holds. A headless host gets it only if it
/// installs a single-threaded context — and xunit does not: by the time a walk parks, the ambient context is
/// gone, so <see cref="Continue"/>'s two <c>TrySetResult</c> calls hand the decider and a frozen newcomer to the
/// thread pool and TWO walks run concurrently over state that assumes one. (This header used to claim "the test
/// thread in headless tests", which is what made the hazard invisible; it cost an intermittent lost
/// <c>Debug.Print</c> — issue #139.) A future headless host that attaches a controller and resumes two
/// activations must supply the single thread itself.</para>
///
/// FREEZE model: the ONE activation that decides to break parks on its own per-break TCS (<c>_breakTcs</c>); any
/// NEWCOMER activation (a Timer tick / control event that fires while paused) parks on a shared resume gate
/// (<c>_resumeGate</c>). The break-frame never awaits the resume gate, which is what avoids a deadlock. Continue
/// opens both (break-frame first); a reset resumes them so their gate re-checks <see cref="IsAborting"/> and
/// raises <see cref="StopExecutionSignal"/> to unwind.
/// </summary>
public sealed class DebugController : IDebugController
{
    private const long YieldIntervalMs = 15;   // time-slice: yield the UI thread at least this often so Ctrl+Break lands

    private readonly Dictionary<string, HashSet<int>> _breakpoints = new(StringComparer.OrdinalIgnoreCase);
    private bool _anyBreakpoints;

    // Break-type watches (Break When True / Changed), evaluated against the executing frame at every statement while
    // running. Gated behind _anyWatchBreaks so the common (no-watch) hot path pays nothing.
    private sealed class WatchBreakState
    {
        public WatchBreakState(string expression, bool breakOnChange) { Expression = expression; BreakOnChange = breakOnChange; }
        public string Expression { get; }
        public bool BreakOnChange { get; }
        public string? LastValue { get; set; }   // Break-When-Changed baseline
        public bool HasBaseline { get; set; }
    }
    private List<WatchBreakState> _watchBreaks = new();
    private bool _anyWatchBreaks;

    // One-shot Run-To-Cursor target (module, 1-based line): a temporary breakpoint the gate breaks on once, then
    // clears. Null when not armed. Cleared by Reset (a fresh run carries no stale target).
    private (string Module, int Line)? _runToTarget;

    private DebugState _state = DebugState.Running;
    private bool _isAborting;
    private bool _pauseRequested;
    // One-shot step (Into/Over/Out): the next qualifying statement breaks, then disarms. Over/Out compare the
    // executing frame's Depth to the depth captured when the step was armed.
    private StepMode _stepMode;
    private int _stepTargetDepth;
    private enum StepMode { None, Into, Over, Out }
    private bool _sessionActive;   // true from a run's Reset() until its Stop() — i.e. running OR paused
    private StoppedInfo? _currentStop;
    private IDebugFrame? _currentFrame;   // the paused activation (captured at break, read for Locals; null unless Paused)

    private TaskCompletionSource? _breakTcs;    // the deciding activation suspends here (non-null while Paused)
    private TaskCompletionSource? _resumeGate;  // newcomers freeze here while Paused

    private long _lastYieldTick = Environment.TickCount64;
    private int _session;   // run generation; a walk parked from a prior session is killed when it resumes

    public DebugState State => _state;
    public bool IsAborting => _isAborting;
    public bool IsSessionActive => _sessionActive;
    public StoppedInfo? CurrentStop => _currentStop;

    public event Action<StoppedInfo>? Stopped;
    public event Action? Continued;

    /// <summary>Prepare for a fresh run: back to Running, clear paused/aborting state. Breakpoints persist (the
    /// front-end owns them). The IDE calls this at the start of every run; a fresh controller already starts here.</summary>
    public void Reset()
    {
        // A new run is a new session generation. Any walk still parked from a prior session (e.g. Restart pressed
        // while paused, or a form closed while paused) must die when it resumes — the post-await session check does
        // that. Release the old gates here so such a walk actually resumes (and unwinds) instead of leaking.
        _session++;
        var bt = _breakTcs;
        var rg = _resumeGate;
        _state = DebugState.Running;
        _isAborting = false;
        _pauseRequested = false;
        _stepMode = StepMode.None;
        _sessionActive = true;   // a run is now active (running/paused) until Stop()
        _currentStop = null;
        _currentFrame = null;
        _breakTcs = null;
        _resumeGate = null;
        _lastYieldTick = Environment.TickCount64;
        // Re-baseline Break-When-Changed watches: a stale baseline from a prior run must not carry into this one.
        // The specs persist (like breakpoints — the front-end re-pushes at run start); only the baselines reset.
        foreach (var w in _watchBreaks) { w.HasBaseline = false; w.LastValue = null; }
        _runToTarget = null;   // a one-shot Run-To-Cursor target doesn't carry into a fresh run
        bt?.TrySetResult();
        rg?.TrySetResult();
    }

    public void SetBreakpoints(string module, IReadOnlyCollection<int> lines)
    {
        if (lines.Count == 0)
            _breakpoints.Remove(module);
        else
            _breakpoints[module] = new HashSet<int>(lines);
        _anyBreakpoints = _breakpoints.Count > 0;
    }

    public void ClearBreakpoints()
    {
        _breakpoints.Clear();
        _anyBreakpoints = false;
    }

    public void RunToCursor(string module, int line) => _runToTarget = (module, line);

    public void SetWatchBreaks(IReadOnlyList<WatchBreakSpec> watches)
    {
        // Merge: preserve the last-seen value/baseline for any (Expression, BreakOnChange) still present, so editing
        // an unrelated watch mid-run doesn't reset a Break-When-Changed baseline (which would miss the next change).
        var merged = new List<WatchBreakState>(watches.Count);
        foreach (var w in watches)
        {
            var prior = _watchBreaks.Find(p => p.BreakOnChange == w.BreakOnChange
                && string.Equals(p.Expression, w.Expression, StringComparison.OrdinalIgnoreCase));
            merged.Add(prior ?? new WatchBreakState(w.Expression, w.BreakOnChange));
        }
        _watchBreaks = merged;
        _anyWatchBreaks = merged.Count > 0;
    }

    public ValueTask OnStatementAsync(int line, string module, IDebugFrame? frame)
    {
        if (_isAborting)
            throw new StopExecutionSignal();
        // Hot path: running, nothing armed, no break-watches to evaluate, not time to yield — no async machinery,
        // no allocation. Break-watches force the slow (async) path because evaluating them is itself async.
        if (_state == DebugState.Running && !ShouldBreak(line, module, frame) && !_anyWatchBreaks && !YieldDue())
            return ValueTask.CompletedTask;
        return SlowAsync(line, module, frame);
    }

    public ValueTask EnterBreakFromStopStatementAsync(int line, string module, IDebugFrame? frame)
    {
        if (_isAborting)
            throw new StopExecutionSignal();
        return SuspendAtBreakAsync(StopReason.StopStatement, line, module, frame);
    }

    /// <summary>The paused frame's Locals, or null when not in Break mode. The walk is frozen while Paused, so the
    /// lazy reads a node's <c>Expand()</c> performs are stable.</summary>
    public DebugScope? GetLocals()
        => _state == DebugState.Paused && _currentFrame is { } f ? f.GetLocals() : null;

    /// <summary>Evaluate an Immediate expression against the paused frame (null when not in Break mode). Runs on the
    /// caller's (UI) thread while the walk is frozen; the frame rejects user-procedure calls so this can't re-enter
    /// the gate.</summary>
    public async Task<string?> EvaluateAsync(string expression)
        => _state == DebugState.Paused && _currentFrame is { } f ? await f.EvaluateAsync(expression) : null;

    /// <summary>Typed Watch evaluation against the paused frame (null when not in Break mode). Same freeze contract
    /// as <see cref="EvaluateAsync"/> — runs on the UI thread while the walk is suspended.</summary>
    public async Task<DebugEvalResult?> EvaluateWatchAsync(string expression)
        => _state == DebugState.Paused && _currentFrame is { } f ? await f.EvaluateTypedAsync(expression) : null;

    /// <summary>The Call Stack (deepest/current frame first), or empty when not in Break mode.</summary>
    public IReadOnlyList<CallStackFrame> GetCallStack()
        => _state == DebugState.Paused && _currentFrame is { } f ? f.GetCallStack() : System.Array.Empty<CallStackFrame>();

    private async ValueTask SlowAsync(int line, string module, IDebugFrame? frame)
    {
        // A newcomer arriving while Paused freezes on the resume gate (VB6 Break freezes the whole program).
        while (_state == DebugState.Paused && _resumeGate is { } gate)
        {
            var session = _session;
            await gate.Task;
            if (_isAborting || _session != session)
                throw new StopExecutionSignal();
        }

        if (_state == DebugState.Running)
        {
            // Evaluate break-type watches against the executing frame (async; only when any are set, and only with a
            // frame — module top-level code has none). A tripped watch breaks, but a real breakpoint at this line
            // still wins the reason.
            bool watchTripped = _anyWatchBreaks && frame is not null && await EvaluateWatchBreaksAsync(frame);

            bool runToHit = MatchesRunTo(line, module);

            if (ShouldBreak(line, module, frame) || watchTripped)
            {
                // Breakpoint wins (VB6 shows a breakpoint hit even mid-step); then Run To Cursor (a temp breakpoint);
                // then a watch; then a step; then pause.
                var reason = MatchesBreakpoint(line, module) || runToHit ? StopReason.Breakpoint
                           : watchTripped ? StopReason.Watch
                           : _stepMode != StepMode.None ? StopReason.Step
                           : StopReason.Pause;
                if (runToHit) _runToTarget = null;   // one-shot temp breakpoint reached
                _pauseRequested = false;
                _stepMode = StepMode.None;   // one-shot — F8 breaks once; press again to step again
                await SuspendAtBreakAsync(reason, line, module, frame);
                return;
            }
        }

        // Running path: time-slice a real yield so a queued Ctrl+Break (dispatched on the same UI thread) can run.
        if (YieldDue())
        {
            _lastYieldTick = Environment.TickCount64;
            await Task.Yield();
        }
    }

    private async ValueTask SuspendAtBreakAsync(StopReason reason, int line, string module, IDebugFrame? frame)
    {
        _state = DebugState.Paused;
        _currentStop = new StoppedInfo(reason, module, line);
        // Capture the DECIDING activation's frame for Locals. Only this activation reaches here (newcomers freeze on
        // the resume gate above), so there is never a clobber; cleared when the break ends (Continue/Step/Stop/Reset).
        _currentFrame = frame;
        // Held in a LOCAL as well as the field, and awaited through the local below. `Stopped` is invoked
        // synchronously on this thread, and a handler that resumes — Continue, Step, Stop, Reset — nulls
        // `_breakTcs` on its way through. Awaiting the field after that is a NullReferenceException, which
        // is what CI was intermittently seeing: it needs the resume to land between the invoke and the
        // await, so it reproduces on a busy multi-core runner and almost never on a developer box.
        //
        // This is the same capture-then-clear idiom the four resume paths already use (`var bt = _breakTcs`
        // before `_breakTcs = null`); only this side was reading the field twice.
        var breakTcs = _breakTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Create the resume gate only once per paused session (NOT per break): a Step Into re-breaks without
        // completing it, so an event frozen while paused must stay parked on the SAME gate across the step —
        // otherwise recreating it here would orphan that frozen newcomer. Continue/Stop/Reset complete + null it.
        _resumeGate ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = _session;
        Stopped?.Invoke(_currentStop.Value);   // synchronous, on the walk's thread
        await breakTcs.Task;
        // Abort if Stop() aborted us OR a Reset() started a new session while we were parked. The session check —
        // not _isAborting — is what defeats the Restart-while-paused race: RestartProject calls Stop() then
        // Reset() in one UI turn, and Reset clears _isAborting BEFORE this posted continuation runs, so without
        // the session guard the stale walk would resume onto the new run's shared controller.
        if (_isAborting || _session != session)
            throw new StopExecutionSignal();
    }

    private bool ShouldBreak(int line, string module, IDebugFrame? frame)
        => _pauseRequested || StepWantsBreak(frame) || MatchesBreakpoint(line, module) || MatchesRunTo(line, module);

    // Whether the armed step wants to break at the current statement. Into: always (next statement, any depth).
    // Over: back at the step's depth or shallower (steps OVER a called proc; a non-call statement is same-depth so
    // it behaves like Into). Out: strictly shallower (the stepped proc has returned). A null frame (no depth) breaks.
    private bool StepWantsBreak(IDebugFrame? frame) => _stepMode switch
    {
        StepMode.Into => true,
        StepMode.Over => frame is null || frame.Depth <= _stepTargetDepth,
        StepMode.Out  => frame is null || frame.Depth <  _stepTargetDepth,
        _ => false,
    };

    private bool MatchesBreakpoint(int line, string module)
        => _anyBreakpoints && _breakpoints.TryGetValue(module, out var set) && set.Contains(line);

    private bool MatchesRunTo(int line, string module)
        => _runToTarget is (var m, var l) && l == line && string.Equals(m, module, StringComparison.OrdinalIgnoreCase);

    // Evaluate each break-type watch against the executing frame. Break-When-True trips whenever the expression
    // coerces to True (level-triggered — VB6: "break when the expression evaluates to True"); Break-When-Changed
    // trips when the displayed value differs from the previous statement's, then updates the baseline. A watch that
    // can't evaluate here (out of scope / error) doesn't trip and leaves its baseline untouched. Every watch is
    // evaluated (not short-circuited) so a Break-When-Changed baseline stays current even when another already
    // tripped. Snapshots the list so a mid-evaluation re-push (SetWatchBreaks replaces the field) is safe.
    private async ValueTask<bool> EvaluateWatchBreaksAsync(IDebugFrame frame)
    {
        var watches = _watchBreaks;
        bool tripped = false;
        foreach (var w in watches)
        {
            DebugEvalResult r;
            try { r = await frame.EvaluateTypedAsync(w.Expression); }
            catch { continue; }   // defensive — EvaluateTypedAsync already swallows its own errors
            if (!r.Ok)
                continue;
            if (w.BreakOnChange)
            {
                if (w.HasBaseline && !string.Equals(r.Display, w.LastValue, StringComparison.Ordinal))
                    tripped = true;
                w.LastValue = r.Display;
                w.HasBaseline = true;
            }
            else if (r.Truthy)
            {
                tripped = true;
            }
        }
        return tripped;
    }

    private bool YieldDue() => Environment.TickCount64 - _lastYieldTick > YieldIntervalMs;

    public void Continue()
    {
        if (_state != DebugState.Paused)
            return;
        var bt = _breakTcs;
        var rg = _resumeGate;
        _state = DebugState.Running;
        _stepMode = StepMode.None;   // a plain Continue (F5) from a step break runs free
        _currentStop = null;
        _currentFrame = null;
        _breakTcs = null;
        _resumeGate = null;
        _lastYieldTick = Environment.TickCount64;
        Continued?.Invoke();
        bt?.TrySetResult();   // resume the decider FIRST (schedules its continuation ahead)
        rg?.TrySetResult();   // then release frozen newcomers
    }

    public void Pause()
    {
        // No-op if not running (idle-between-events pause is a documented no-op; the next statement gate breaks).
        if (_state == DebugState.Running)
            _pauseRequested = true;
    }

    public void StepInto()
    {
        // Break at the next executed statement — this frame OR a callee's first statement (descends into calls).
        _stepMode = StepMode.Into;
        ResumeForStep();
    }

    /// <summary>Step Over (Shift+F8): run any called procedure to completion and break at the next statement in the
    /// current frame (or shallower). On a non-call statement it behaves like Step Into.</summary>
    public void StepOver() => ArmDepthStep(StepMode.Over);

    /// <summary>Step Out (Ctrl+Shift+F8): run until the current procedure returns, then break in its caller.</summary>
    public void StepOut() => ArmDepthStep(StepMode.Out);

    public bool SetNextStatement(string module, int line)
    {
        // Only while paused, only within the paused frame's own module (the current procedure), and only if the frame
        // accepts the move (a top-level target, paused at a top-level statement). On success, move the amber
        // current-statement bar by re-publishing Stopped at the new line — the walk stays parked on its break gate;
        // the pc repoint applies inside the interpreter loop when it resumes.
        if (_state != DebugState.Paused || _currentFrame is not { } f || _currentStop is not { } stop)
            return false;
        if (!string.Equals(stop.Module, module, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!f.SetNextStatement(line))
            return false;
        _currentStop = new StoppedInfo(stop.Reason, module, line);
        Stopped?.Invoke(_currentStop.Value);   // front-ends move the current-line bar; Locals/Watches re-read (state unchanged)
        return true;
    }

    // Over/Out compare against the CURRENT call depth, which is only available while paused (we hold the frame).
    // From idle/running there's no frame, so fall back to Step Into (start-and-step, like F8).
    private void ArmDepthStep(StepMode mode)
    {
        if (_currentFrame is { } f)
        {
            _stepMode = mode;
            _stepTargetDepth = f.Depth;
        }
        else
        {
            _stepMode = StepMode.Into;
        }
        ResumeForStep();
    }

    // Resume the parked decider for a step (Paused → Running), leaving the resume gate intact so an event frozen
    // while paused stays frozen (not stolen by the step) until a real Continue. No Continued event — the amber bar
    // simply MOVES on the next Stopped. Idle/Running ⇒ leaves the step armed (the next gate breaks).
    private void ResumeForStep()
    {
        if (_state != DebugState.Paused)
            return;
        var bt = _breakTcs;
        _state = DebugState.Running;
        _currentStop = null;
        _currentFrame = null;
        _breakTcs = null;
        _lastYieldTick = Environment.TickCount64;
        bt?.TrySetResult();   // resume the decider; _resumeGate is intentionally NOT completed
    }

    public void NotifyDispatchIdle()
    {
        // A dispatch (event-handler chain) returned to idle without the armed step ever breaking. Disarm it so a
        // leftover Step Into/Over/Out can't fire on the NEXT, unrelated event handler. Only while Running — never
        // disturb a step armed for a chain that is currently paused mid-flight (Paused), nor after Stop (Stopped).
        if (_state == DebugState.Running)
            _stepMode = StepMode.None;
    }

    public void Stop()
    {
        if (_state == DebugState.Stopped)
            return;
        _isAborting = true;
        var bt = _breakTcs;
        var rg = _resumeGate;
        _state = DebugState.Stopped;
        _stepMode = StepMode.None;
        _sessionActive = false;   // the run has ended
        _currentStop = null;
        _currentFrame = null;
        _breakTcs = null;
        _resumeGate = null;
        // Leaving break mode (ended): tell subscribers so the editor clears the current-statement bar and the run
        // commands re-query — the same signal Continue() raises. Without this, ending while paused would leave the
        // amber bar stuck (nothing else clears it).
        Continued?.Invoke();
        // Resume any suspended activation so its gate re-checks IsAborting and raises StopExecutionSignal to unwind.
        bt?.TrySetResult();
        rg?.TrySetResult();
    }
}
