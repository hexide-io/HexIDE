using HexIDE.Addins;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using static HexIDE.Addins.AddinRunEngineService;

namespace HexIDE.IDE;

/// <summary>
/// The run engine the IDE is composed with (#5616): it forwards to the engine an owner registered through the host, and
/// answers as no engine does while none is registered.
/// </summary>
/// <remarks>
/// <para>
/// <b>With no engine</b> nothing runs: <see cref="Runs"/> answers false, so a start is logged and nothing else happens, as for
/// a project no engine runs.
/// </para>
/// <para>
/// <b>It carries only how the IDE is wired, never a copy of editor state.</b> <see cref="HaltHost"/>, the handlers of
/// <see cref="ErrorMet"/> and <see cref="ErrorTrapping"/> are set once at composition, before any engine registers, so they're
/// held and given to the engine that does. Breakpoints are not: the runner pushes the editor's whole set at every start.
/// </para>
/// <para>
/// <b>An engine's throws are contained:</b> logged, and the call answers as with no engine.
/// </para>
/// </remarks>
internal sealed class RunEngineForwarder : IRunEngine, IDesignerRunEngine
{
    private readonly AddinRunEngineService engines;
    private readonly IDebugController debugger;
    private Func<RunFailure, Task<bool>>? haltHost;
    private ErrorTrapping errorTrapping = SettingsDefaults.ErrorTrapping;

    public RunEngineForwarder(AddinRunEngineService engines, IDebugController debugger)
    {
        this.engines = engines;
        this.debugger = debugger;
        engines.Attached += Attach;
        engines.Detached += Detach;
        if (engines.Current is { } registered)
            Attach(registered);
    }

    /// <inheritdoc />
    public event Action<RunFailure>? ErrorMet;

    /// <inheritdoc />
    public Func<RunFailure, Task<bool>>? HaltHost
    {
        get => haltHost;
        set
        {
            haltHost = value;
            if (engines.Current is { } registered)
                Call(registered, () => registered.Engine.HaltHost = value, nameof(HaltHost));
        }
    }

    /// <inheritdoc />
    public ErrorTrapping ErrorTrapping
    {
        get => errorTrapping;
        set
        {
            errorTrapping = value;
            if (engines.Current is { } registered)
                Call(registered, () => registered.Engine.ErrorTrapping = value, nameof(ErrorTrapping));
        }
    }

    /// <inheritdoc />
    /// <remarks>The IDE's debugger, which forwards to the registered engine's (<see cref="DebuggerForwarder"/>).</remarks>
    public IDebugController Debugger => debugger;

    /// <inheritdoc />
    public IRunningProgram Program => RunningProgram.Current;

    /// <inheritdoc />
    public bool Runs(ProjectDefinition project) =>
        Starting() is { } registered && Call(registered, () => registered.Engine.Runs(project), false, nameof(Runs));

    /// <inheritdoc />
    public string? RefusesToStart(ProjectDefinition project) =>
        Starting() is { } registered
            ? Call(registered, () => registered.Engine.RefusesToStart(project), null, nameof(RefusesToStart))
            : null;

    /// <inheritdoc />
    /// <remarks>
    /// A start the engine throws on ends at once and is logged, as a throw from a start was before engines registered: no
    /// dialog, and the runner tears the start down.
    /// </remarks>
    public RunStart Prepare(ProjectDefinition project)
    {
        if (Starting() is not { } registered)
            return RunStart.Started(EndedRun.Instance);
        var start = Call(registered, () => registered.Engine.Prepare(project), null, nameof(Prepare));
        if (start is null)
            return RunStart.Started(EndedRun.Instance);
        if (start.Run is not { } run)
            return start;
        var tracked = new TrackedRun(registered, run);
        registered.ActiveRun = tracked;
        return RunStart.Started(tracked);
    }

    /// <summary>
    /// Raised once an engine is registered or let go (#5633), on the registering thread or the UI thread: what the designer
    /// asked the engine may answer differently now. Handlers only post.
    /// </summary>
    internal event Action? DesignerChanged;

    /// <inheritdoc />
    /// <remarks>
    /// With no engine, one being withdrawn, or one that doesn't preview, nothing is shown, and that's logged. A throw is logged
    /// as the engine's other throws are, and its message is answered, so the designer shows it in the box a refused preview
    /// is shown in.
    /// </remarks>
    public string? Preview(FormDefinition form)
    {
        if (Starting() is not { Engine: IDesignerRunEngine designer } registered)
        {
            Serilog.Log.Information("Run engine: no engine previews forms, so {Form} isn't previewed", form.Name);
            return null;
        }
        try
        {
            return designer.Preview(form);
        }
        catch (Exception ex)
        {
            Contained(registered, ex, nameof(Preview));
            return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        }
    }

    /// <inheritdoc />
    /// <remarks>False with no engine, one being withdrawn, or one that doesn't say, and when it throws.</remarks>
    public bool RunsWhenCarried(string type, bool holdsControls, bool inArray) =>
        Starting() is { Engine: IDesignerRunEngine designer } registered
        && Call(registered, () => designer.RunsWhenCarried(type, holdsControls, inArray), false, nameof(RunsWhenCarried));

    // The registration that may start a run: none once its handle is disposed, though it's still let go only later.
    private AddinRunEngineService.Registration? Starting() =>
        engines.Current is { Withdrawing: false } registered ? registered : null;

    private void Attach(AddinRunEngineService.Registration registered)
    {
        Call(registered, () => registered.Engine.ErrorMet += Relay, nameof(ErrorMet));
        Call(registered, () => registered.Engine.HaltHost = haltHost, nameof(HaltHost));
        Call(registered, () => registered.Engine.ErrorTrapping = errorTrapping, nameof(ErrorTrapping));
        DesignerChanged?.Invoke();
    }

    private void Detach(AddinRunEngineService.Registration registered)
    {
        Call(registered, () => registered.Engine.ErrorMet -= Relay, nameof(ErrorMet));
        Call(registered, () => registered.Engine.HaltHost = null, nameof(HaltHost));
        DesignerChanged?.Invoke();
    }

    private void Relay(RunFailure failure) => ErrorMet?.Invoke(failure);

    // A run that has already ended, with nothing to report.
    private sealed class EndedRun : IEngineRun
    {
        internal static readonly EndedRun Instance = new();

        public Task<RunFailure?> RunToEndAsync() => Task.FromResult<RunFailure?>(null);

        public void End()
        {
        }
    }
}
