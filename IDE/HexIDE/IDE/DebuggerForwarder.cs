using HexIDE.Addins;
using HexIDE.Runtime.Debugging;
using static HexIDE.Addins.AddinRunEngineService;

namespace HexIDE.IDE;

/// <summary>
/// The debugger the IDE is composed with (#5616): it forwards to the debugger of the engine an owner registered through the
/// host, and answers as an idle debugger does while none is registered.
/// </summary>
/// <remarks>
/// <para>
/// <b>With no engine</b> it answers what a debugger that has never run a program answers: <see cref="DebugState.Running"/>, no
/// session and no stop, nothing to evaluate, and an empty call stack. What it's told is dropped: breakpoints, watches and
/// error trapping are pushed again, in full, at every start.
/// </para>
/// <para>
/// <b>An engine's throws are contained:</b> logged, and the call answers as with no engine.
/// </para>
/// </remarks>
internal sealed class DebuggerForwarder : IDebugController
{
    private readonly AddinRunEngineService engines;

    public DebuggerForwarder(AddinRunEngineService engines)
    {
        this.engines = engines;
        engines.Attached += Attach;
        engines.Detached += Detach;
        if (engines.Current is { } registered)
            Attach(registered);
    }

    /// <inheritdoc />
    public event Action<StoppedInfo>? Stopped;

    /// <inheritdoc />
    public event Action? Continued;

    /// <inheritdoc />
    public DebugState State => Ask(d => d.State, DebugState.Running);

    /// <inheritdoc />
    public bool IsSessionActive => Ask(d => d.IsSessionActive, false);

    /// <inheritdoc />
    public StoppedInfo? CurrentStop => Ask(d => d.CurrentStop, null);

    /// <inheritdoc />
    public void Reset() => Tell(d => d.Reset());

    /// <inheritdoc />
    public void SetBreakpoints(string module, IReadOnlyCollection<int> lines) => Tell(d => d.SetBreakpoints(module, lines));

    /// <inheritdoc />
    public void SetBreakpointConditions(string module, IReadOnlyDictionary<int, string> conditions) =>
        Tell(d => d.SetBreakpointConditions(module, conditions));

    /// <inheritdoc />
    public void ClearBreakpoints() => Tell(d => d.ClearBreakpoints());

    /// <inheritdoc />
    public void SetWatchBreaks(IReadOnlyList<WatchBreakSpec> watches) => Tell(d => d.SetWatchBreaks(watches));

    /// <inheritdoc />
    public void RunToCursor(string module, int line) => Tell(d => d.RunToCursor(module, line));

    /// <inheritdoc />
    public void Continue() => Tell(d => d.Continue());

    /// <inheritdoc />
    public void Pause() => Tell(d => d.Pause());

    /// <inheritdoc />
    public void StepInto() => Tell(d => d.StepInto());

    /// <inheritdoc />
    public void StepOver() => Tell(d => d.StepOver());

    /// <inheritdoc />
    public void StepOut() => Tell(d => d.StepOut());

    /// <inheritdoc />
    public bool SetNextStatement(string module, int line) => Ask(d => d.SetNextStatement(module, line), false);

    /// <inheritdoc />
    public void Stop() => Tell(d => d.Stop());

    /// <inheritdoc />
    public DebugScope? GetLocals() => Ask(d => d.GetLocals(), null);

    /// <inheritdoc />
    public Task<string?> EvaluateAsync(string expression) => AskAsync(d => d.EvaluateAsync(expression));

    /// <inheritdoc />
    public Task<DebugEvalResult?> EvaluateWatchAsync(string expression) => AskAsync(d => d.EvaluateWatchAsync(expression));

    /// <inheritdoc />
    public Task<DebugEvalResult?> EvaluateScopedWatchAsync(string expression, string context) =>
        AskAsync(d => d.EvaluateScopedWatchAsync(expression, context));

    /// <inheritdoc />
    public IReadOnlyList<CallStackFrame> GetCallStack() => Ask(d => d.GetCallStack(), []);

    private T Ask<T>(Func<IDebugController, T> question, T idle, [System.Runtime.CompilerServices.CallerMemberName] string member = "") =>
        engines.Current is { } registered
            ? Call(registered, () => question(registered.Engine.Debugger), idle, "Debugger." + member)
            : idle;

    private void Tell(Action<IDebugController> command, [System.Runtime.CompilerServices.CallerMemberName] string member = "")
    {
        if (engines.Current is { } registered)
            Call(registered, () => command(registered.Engine.Debugger), "Debugger." + member);
    }

    private async Task<T?> AskAsync<T>(Func<IDebugController, Task<T?>> question,
        [System.Runtime.CompilerServices.CallerMemberName] string member = "") where T : class
    {
        if (engines.Current is not { } registered)
            return null;
        try
        {
            return await question(registered.Engine.Debugger);
        }
        catch (Exception ex)
        {
            Contained(registered, ex, "Debugger." + member);
            return null;
        }
    }

    private void Attach(AddinRunEngineService.Registration registered) =>
        Call(registered, () =>
        {
            registered.Engine.Debugger.Stopped += RelayStopped;
            registered.Engine.Debugger.Continued += RelayContinued;
        }, nameof(Stopped));

    private void Detach(AddinRunEngineService.Registration registered) =>
        Call(registered, () =>
        {
            registered.Engine.Debugger.Stopped -= RelayStopped;
            registered.Engine.Debugger.Continued -= RelayContinued;
        }, nameof(Stopped));

    private void RelayStopped(StoppedInfo stop) => Stopped?.Invoke(stop);

    private void RelayContinued() => Continued?.Invoke();
}
