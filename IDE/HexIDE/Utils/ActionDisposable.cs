namespace HexIDE.Utils;

public sealed class ActionDisposable(Action onDispose) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        onDispose();
    }
}
