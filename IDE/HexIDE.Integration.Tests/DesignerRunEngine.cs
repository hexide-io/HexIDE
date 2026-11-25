using System;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Runtime.Running;

namespace HexIDE.Integration.Tests;

/// <summary>
/// The run engine the designer asks, as the IDE composes it (#5633): the forwarder, with an engine registered through the
/// engine service as the Basic provider registers its interpreter. Disposing it withdraws the engine and runs the posted
/// withdrawal, so call it on the UI thread.
/// </summary>
internal sealed class DesignerRunEngine : IDisposable
{
    internal static readonly AddinOrigin Provider = new(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };

    private readonly IRunningProgram programBefore = RunningProgram.Current;
    private IDisposable? registration;

    /// <param name="dialogs">The IDE's window-maker the engine is given; a substitute when null.</param>
    /// <param name="engine">The engine registered; the provider's interpreter when null. Null registers none.</param>
    /// <param name="register">Whether to register one at all.</param>
    internal DesignerRunEngine(IProgramDialogWindows? dialogs = null, Func<RunEngineServices, IRunEngine>? engine = null,
        bool register = true)
    {
        Dialogs = dialogs ?? Substitute.For<IProgramDialogWindows>();
        Engines = new AddinRunEngineService(() => TestRunners.Services(Substitute.For<IProgramHost>(), windows: Dialogs));
        Forwarder = new RunEngineForwarder(Engines, new DebuggerForwarder(Engines));
        if (register)
            Register(engine);
    }

    internal IProgramDialogWindows Dialogs { get; }

    internal AddinRunEngineService Engines { get; }

    internal RunEngineForwarder Forwarder { get; }

    internal void Register(Func<RunEngineServices, IRunEngine>? engine = null) =>
        registration = Engines.Register(engine ?? (services => new InterpreterRunEngine(services)), Provider);

    /// <summary>Withdraws the engine, and runs the withdrawal the handle posts.</summary>
    internal void Withdraw()
    {
        registration?.Dispose();
        registration = null;
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        Withdraw();
        RunningProgram.Current = programBefore;
    }
}
