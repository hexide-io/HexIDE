using HexIDE;

namespace HexIDE.Tests;

// xUnit awaits this owner after each test, including a failed assertion (#2588).
public abstract class CompositionTestBase : IAsyncDisposable
{
    private DISetup? composition;

    protected DISetup Composition => composition ??= new DISetup();

    public async ValueTask DisposeAsync()
    {
        if (composition is not null)
            await composition.LspClient.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
