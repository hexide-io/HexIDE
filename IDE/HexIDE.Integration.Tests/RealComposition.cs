using System;
using System.Threading.Tasks;

namespace HexIDE.Integration.Tests;

/// <summary>
/// The application's real composition for one test. Disposing it disposes the composition's language-server client.
/// </summary>
/// <remarks>
/// <para>
/// The client is the router that starts the bundled VB6 server for the first VB6 document a test opens. Dropped
/// undisposed, it leaves that server running for the rest of the suite. Its connection keeps reading on pool threads,
/// and those post to <c>Dispatcher.UIThread</c> between tests. That's the race behind #2530's hangs:
/// Avalonia.Headless 12.0.x clears the UI dispatcher as each test's setup starts, and a pool thread that reaches the
/// dispatcher first takes it.
/// </para>
/// <para>
/// Only the client is disposed. The whole composition would dispose every singleton it built, so this keeps to
/// the narrower step these tests already took. <see cref="LanguageServerLeakGuard"/> fails the run on a server still
/// running at the end.
/// </para>
/// </remarks>
internal sealed class RealComposition : IAsyncDisposable
{
    public DISetup Composition { get; } = new();

    // As HexIDE.Desktop's startup does, the provider registers its controls and toolbox through the host an add-in is given
    // (#5594): the composition's registry starts empty.
    public RealComposition()
    {
        var host = new HexIDE.Addins.AddinOwnedHost(Composition.HexIdeHost, null!, HexIDE.Desktop.BasicProviderComposition.Origin);
        foreach (var component in HexIDE.Runtime.BasicProvider.Components)
            host.RegisterComponent(component);
        foreach (var (component, iconKey) in HexIDE.Runtime.BasicProvider.Toolbox)
            host.RegisterToolboxItem(component, iconKey);
        foreach (var library in HexIDE.Runtime.BasicProvider.Libraries)
            host.RegisterControlLibrary(library, () => HexIDE.Runtime.BasicProvider.ControlsOf(library));
        host.RegisterLanguageEditing(HexIDE.Runtime.BasicProvider.LanguageEditing);
        // And its interpreter, through the same host (#5616): with none, the composition runs nothing.
        engineRegistration = host.RegisterRunEngine(services => Engine = new HexIDE.Runtime.Running.InterpreterRunEngine(services));
        // VB6's projects, as the provider registers them (#5625).
        host.RegisterProjectType(HexIDE.Runtime.Serialization.Vb6ProjectFileFormat.ProjectType());
    }

    private readonly IDisposable engineRegistration;
    private readonly HexIDE.IDE.IRunningProgram programBefore = HexIDE.IDE.RunningProgram.Current;

    /// <summary>The interpreter the provider registered, whose debugger the composition's controller forwards to.</summary>
    public HexIDE.Runtime.Running.InterpreterRunEngine Engine { get; private set; } = null!;

    public ValueTask DisposeAsync()
    {
        engineRegistration.Dispose();
        HexIDE.IDE.RunningProgram.Current = programBefore;
        return Composition.LspClient.DisposeAsync();
    }
}
