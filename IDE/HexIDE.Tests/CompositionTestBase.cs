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

    /// <summary>
    /// Whether the composition gets VB6's project type as the Basic provider registers it at start-up (#5625). A test of
    /// that registration itself turns it off and registers it, or not, as it needs.
    /// </summary>
    protected virtual bool RegistersVb6 => true;

    // The Basic provider's interpreter (#5616) and VB6's project type (#5625), through its owned host, as HexIDE.Desktop
    // registers them at start-up.
    private DISetup Compose()
    {
        var setup = new DISetup();
        var provider = new AddinOrigin(RegistrationGrants.BasicProvider, "HexIDE.Basic") { IsComposed = true };
        var owned = new AddinOwnedHost(setup.HexIdeHost, null!, provider);
        engineRegistration = ((IRunEngineRegistration)owned).RegisterRunEngine(services => Engine = new InterpreterRunEngine(services));
        if (RegistersVb6)
            owned.RegisterProjectType(HexIDE.Runtime.Serialization.Vb6ProjectFileFormat.ProjectType());
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
