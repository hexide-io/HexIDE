using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

// A rename retires a name; an ordinary editor close does not. Queue delivery in acceptance order
// so a publication accepted before rename is cleared after delivery, without calling handlers under a lock.
internal sealed class DiagnosticLifecycle(ILogger logger)
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

    // A handler can wait for a rename on another thread. Waiting for its clear here would
    // deadlock: the clear is ordered after that handler, but may run after Rename returns.
    public void Rename(string uri, Action clear) => Enqueue(uri, clear, rename: true);

    // Queued now, under whatever lock the caller holds, and delivered by DeliverQueued once the caller has let go of it:
    // its place in the order is fixed where the caller decides it, and no handler runs under the caller's lock. True
    // when the caller is the one to deliver; otherwise a delivery already running reaches it.
    public bool Queue(Action action)
    {
        lock (_gate)
        {
            _pending.Enqueue(action);
            if (_delivering) return false;
            _delivering = true;
            return true;
        }
    }

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

        DeliverQueued();
    }

    public void DeliverQueued()
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
            try { next(); }
            catch (Exception ex) { logger.LogDebug(ex, "A diagnostic lifecycle handler threw"); }
        }
    }
}
