using Avalonia.Threading;
using HexIDE.IDE;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// The engine that runs projects in the IDE: the one an owner registered (#5616), which the IDE's Run, debugger and
/// automation reach through <see cref="RunEngineForwarder"/>, <see cref="DebuggerForwarder"/> and
/// <see cref="RunningProgram.Current"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deny by default.</b> Only an owner whose grant says so may register (<see cref="RegistrationGrant.RunEngine"/>), because
/// the engine runs the developer's code in the IDE's process. Today that's the Basic provider alone.
/// </para>
/// <para>
/// <b>One at a time.</b> While one registration holds, another is refused, naming both owners, so nothing replaces the engine
/// silently. It's held per composition: each IDE the headless tests make has its own.
/// </para>
/// <para>
/// <b>Its code is contained.</b> A throw from the owner's factory refuses the registration; a throw from the engine is logged
/// and the call answers as with no engine (<see cref="Call{T}"/>).
/// </para>
/// <para>
/// <b>Withdrawal never waits.</b> Disposing the handle can happen on any thread, under the add-in registry's lock, so it only
/// refuses new starts and posts the rest to the UI thread: there a program still running is ended as End ends it, and the
/// engine is let go and, if it's <see cref="IDisposable"/>, disposed (<see cref="Withdraw"/>).
/// </para>
/// </remarks>
internal sealed class AddinRunEngineService
{
    private readonly Func<RunEngineServices> services;
    private readonly object gate = new();
    private Registration? current;

    /// <summary>The service DI makes, which gives an engine the IDE's program host, its windows and its settings.</summary>
    internal AddinRunEngineService(IProgramHost programHost, IWindowManager windows, ISettingsService settings)
        : this(() => new RunEngineServices(programHost, windows, () => settings.RDCoreWin64OnlySetOnWindows, () => Static.SingleView))
    {
    }

    /// <summary>A service that gives an engine <paramref name="services"/>; tests make it so.</summary>
    internal AddinRunEngineService(Func<RunEngineServices> services) => this.services = services;

    /// <summary>Who may register what. HexIDE's own table; a test gives its own owners grants here.</summary>
    internal IReadOnlyList<RegistrationGrant> Grants { get; init; } = RegistrationGrants.All;

    /// <summary>Raised, on the registering thread, once an engine is registered.</summary>
    internal event Action<Registration>? Attached;

    /// <summary>Raised, on the UI thread, once a withdrawn engine has been let go.</summary>
    internal event Action<Registration>? Detached;

    /// <summary>The registration that runs projects, or null when none does; still set while one is being withdrawn.</summary>
    internal Registration? Current
    {
        get { lock (gate) return current; }
    }

    internal IDisposable Register(Func<RunEngineServices, IRunEngine> factory, AddinOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (RegistrationGrants.For(origin, Grants) is not { RunEngine: true })
        {
            Log.Warning("Run engine: refused {Addin} ({AddinId}): it holds no grant to run projects", origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title} isn't one HexIDE lets run projects.");
        }
        RefuseWhileHeld(origin);

        IRunEngine engine;
        try
        {
            engine = factory(services()) ?? throw new InvalidOperationException("The factory made no engine.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Run engine: {Addin} ({AddinId}) couldn't make its engine", origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title} couldn't make its run engine.", ex);
        }

        Registration registration;
        lock (gate)
        {
            RefuseWhileHeld(origin);
            current = registration = new Registration(this, origin, engine);
        }
        if (Call<IRunningProgram?>(registration, () => engine.Program, null, nameof(IRunEngine.Program)) is { } program)
        {
            registration.Program = program;
            RunningProgram.Current = program;
        }
        Attached?.Invoke(registration);
        Log.Information("Run engine: {Addin} ({AddinId}) runs projects", origin.Title, origin.Id);
        return registration;
    }

    private void RefuseWhileHeld(AddinOrigin origin)
    {
        if (Current is not { } held)
            return;
        Log.Warning("Run engine: refused {Addin} ({AddinId}): {Owner} ({OwnerId}) runs projects already",
            origin.Title, origin.Id, held.Owner.Title, held.Owner.Id);
        throw new InvalidOperationException(
            $"{origin.Title} can't run projects: {held.Owner.Title} ({held.Owner.Id}) runs them already.");
    }

    // On the UI thread, with no lock held: the program is ended as End ends it (the debugger released first, so a walk
    // paused at a breakpoint unwinds), then the engine is let go.
    private void Withdraw(Registration registration)
    {
        if (registration.ActiveRun is { } run)
        {
            Call(registration, registration.Engine.Debugger.Stop, "Debugger.Stop");
            Call(registration, run.EndInner, nameof(IEngineRun.End));
        }
        lock (gate)
        {
            if (ReferenceEquals(current, registration))
                current = null;
        }
        Detached?.Invoke(registration);
        if (registration.Program is { } program && ReferenceEquals(RunningProgram.Current, program))
            RunningProgram.Current = new EmptyRunningProgram();
        // Last, so nothing it installed outlives it: the Basic provider's puts back the runtime's own dialogs.
        if (registration.Engine is IDisposable disposable)
            Call(registration, disposable.Dispose, nameof(IDisposable.Dispose));
        Log.Information("Run engine: {Addin} ({AddinId}) no longer runs projects", registration.Owner.Title, registration.Owner.Id);
    }

    /// <summary>
    /// Calls the engine, containing what it throws: logged, naming its owner, and answered with <paramref name="fallback"/>,
    /// what the IDE answers with no engine.
    /// </summary>
    internal static T Call<T>(Registration registration, Func<T> call, T fallback, string member)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            Contained(registration, ex, member);
            return fallback;
        }
    }

    /// <summary>Logs what the engine threw from <paramref name="member"/>, naming its owner.</summary>
    internal static void Contained(Registration registration, Exception exception, string member) =>
        Log.Error(exception, "Run engine: {Member} threw in the engine of {Addin} ({AddinId}); answered as with no engine",
            member, registration.Owner.Title, registration.Owner.Id);

    /// <inheritdoc cref="Call{T}"/>
    internal static void Call(Registration registration, Action call, string member) =>
        Call(registration, () => { call(); return true; }, false, member);

    /// <summary>One engine's registration, and the handle that withdraws it.</summary>
    internal sealed class Registration(AddinRunEngineService service, AddinOrigin owner, IRunEngine engine) : IDisposable
    {
        private bool withdrawing;

        internal AddinOrigin Owner => owner;
        internal IRunEngine Engine => engine;

        /// <summary>What the engine said its program is, asked once when it registered.</summary>
        internal IRunningProgram? Program { get; set; }

        /// <summary>The run it started that hasn't ended, which withdrawal ends.</summary>
        internal TrackedRun? ActiveRun { get; set; }

        /// <summary>Whether it's being withdrawn: it starts no more runs from the moment its handle is disposed.</summary>
        internal bool Withdrawing
        {
            get { lock (service.gate) return withdrawing; }
        }

        public void Dispose()
        {
            lock (service.gate)
            {
                if (withdrawing)
                    return;
                withdrawing = true;
            }
            // Never waits: the registry disposes this under its lock, from whatever thread disables the owner.
            Dispatcher.UIThread.Post(() => service.Withdraw(this));
        }
    }

    /// <summary>A run the engine started, followed so withdrawal can end it, its throws contained.</summary>
    internal sealed class TrackedRun(Registration registration, IEngineRun inner) : IEngineRun
    {
        public async Task<RunFailure?> RunToEndAsync()
        {
            try
            {
                return await inner.RunToEndAsync();
            }
            catch (Exception ex)
            {
                // As before the engine registered: what escapes a run is logged, and the run has ended.
                Log.Error(ex, "Run engine: a run of the engine of {Addin} ({AddinId}) threw as it ended",
                    registration.Owner.Title, registration.Owner.Id);
                return null;
            }
            finally
            {
                if (ReferenceEquals(registration.ActiveRun, this))
                    registration.ActiveRun = null;
            }
        }

        public void End() => Call(registration, inner.End, nameof(IEngineRun.End));

        internal void EndInner() => inner.End();
    }
}
