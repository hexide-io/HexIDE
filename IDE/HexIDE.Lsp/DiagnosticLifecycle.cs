namespace HexIDE.Lsp;

// A rename retires a name; an ordinary editor close does not. Queue delivery in acceptance order
// so a publication accepted before rename is cleared after delivery, without calling handlers under a lock.
internal sealed class DiagnosticLifecycle
{
    private readonly object _gate = new();
    private readonly HashSet<string> _renamed = new(LspDocumentUri.Comparer);
    private readonly Queue<Action> _pending = new();
    private bool _delivering;

    public void Open(string uri)
    {
        lock (_gate) _renamed.Remove(uri);
    }

    public void Retire(string uri)
    {
        lock (_gate) _renamed.Add(uri);
    }

    public void Publish(string uri, Action deliver) => Enqueue(uri, deliver, rename: false);

    public void Rename(string uri, Action clear) => Enqueue(uri, clear, rename: true);

    private void Enqueue(string uri, Action action, bool rename)
    {
        lock (_gate)
        {
            if (rename) _renamed.Add(uri);
            else if (_renamed.Contains(uri)) return;
            _pending.Enqueue(action);
            if (_delivering) return;
            _delivering = true;
        }

        try
        {
            while (true)
            {
                Action next;
                lock (_gate)
                {
                    if (!_pending.TryDequeue(out next!))
                    {
                        _delivering = false;
                        return;
                    }
                }
                next();
            }
        }
        catch
        {
            lock (_gate) _delivering = false;
            throw;
        }
    }
}
