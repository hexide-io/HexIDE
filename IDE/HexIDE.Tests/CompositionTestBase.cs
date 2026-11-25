using HexIDE;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime.Running;

namespace HexIDE.Tests;

// xUnit awaits this owner after each test, including a failed assertion (#2588). It calls only DisposeAsync on a class
// that has both, so a test class with cleanup of its own overrides this rather than adding a Dispose.
public abstract class CompositionTestBase : IAsyncDisposable
{
    private DISetup? composition;
    private IDisposable? engineRegistration;
    private readonly IRunningProgram programBefore = RunningProgram.Current;

    protected DISetup Composition => composition ??= Compose();

    /// <summary>The interpreter the Basic provider registers, as HexIDE.Desktop's composition registers it (#5616).</summary>
    protected InterpreterRunEngine? Engine { get; private set; }

    private DISetup Compose()
    {
        var setup = new DISetup();
        var provider = new AddinOrigin(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };
        engineRegistration = ((IRunEngineRegistration)new AddinOwnedHost(setup.HexIdeHost, null!, provider))
            .RegisterRunEngine(services => Engine = new InterpreterRunEngine(services));
        return setup;
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.LspClient.DisposeAsync();
        engineRegistration?.Dispose();
        RunningProgram.Current = programBefore;
        GC.SuppressFinalize(this);
    }
}
