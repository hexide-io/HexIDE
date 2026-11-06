using Serilog;

namespace HexIDE.Addins;

// Initialize is synchronous. Keep its rollback ledger on this thread, without changing the host
// facade (in particular, Options uses the calling assembly to identify its contributor).
internal sealed class AddinInitializationScope : IDisposable
{
    [ThreadStatic] private static AddinInitializationScope? _current;
    private readonly AddinInitializationScope? _previous = _current;
    private readonly List<Action> _undo = [];

    public AddinInitializationScope() => _current = this;

    public static void Record(Action undo) => _current?._undo.Add(undo);

    public void Complete() => _undo.Clear();

    public void Dispose()
    {
        _current = _previous;
        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            try { _undo[i](); }
            catch (Exception ex) { Log.Warning(ex, "AddinRegistry: error rolling back a contribution"); }
        }
        _undo.Clear();
    }
}
