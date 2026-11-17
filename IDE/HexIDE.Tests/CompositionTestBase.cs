using HexIDE;

namespace HexIDE.Tests;

// xUnit awaits this owner after each test, including a failed assertion (#2588). It calls only DisposeAsync on a class
// that has both, so a test class with cleanup of its own overrides this rather than adding a Dispose.
public abstract class CompositionTestBase : IAsyncDisposable
{
    private DISetup? composition;

    protected DISetup Composition => composition ??= new DISetup();

    public virtual async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.LspClient.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
