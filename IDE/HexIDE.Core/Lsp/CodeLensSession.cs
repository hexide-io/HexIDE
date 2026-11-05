using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>One document's lenses and cancellable viewport work. Callers supply zero-based line numbers.</summary>
public sealed class CodeLensSession : IDisposable
{
    private readonly ILspClient client;
    private readonly string uri;
    private readonly object gate = new();

    public CodeLensSession(ILspClient client, string uri)
    {
        this.client = client;
        this.uri = uri;
        client.CodeLensRefreshRequested += OnRefreshRequested;
    }

    public event EventHandler<Exception>? RequestFailed;

    private async void OnRefreshRequested(object? sender, EventArgs args)
    {
        try { await RefreshAsync(); }
        catch (Exception error) { RequestFailed?.Invoke(this, error); }
    }
    private Entry[] entries = [];
    private CancellationTokenSource? discovery;
    private int firstLine = -1;
    private int lastLine = -1;
    private bool disposed;
    private bool suspended;
    private bool current;
    private bool enabled = true;

    public Task SetEnabledAsync(bool value)
    {
        lock (gate)
        {
            if (disposed || enabled == value) return Task.CompletedTask;
            enabled = value;
            if (!value)
            {
                current = false;
                discovery?.Cancel();
                CancelResolutions();
                entries = [];
            }
        }
        if (value) return RefreshAsync();
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Suspend()
    {
        lock (gate) suspended = true;
    }

    public Task ResumeAsync()
    {
        lock (gate)
        {
            suspended = false;
            if (current || discovery is { IsCancellationRequested: false }) return Task.CompletedTask;
        }
        return RefreshAsync();
    }

    private sealed class Entry(CodeLens lens)
    {
        public CodeLens Lens = lens;
        public CancellationTokenSource? Resolution;
        public bool Attempted;
    }

    public CodeLens[] Lenses
    {
        get { lock (gate) return entries.Select(e => e.Lens).ToArray(); }
    }

    /// <summary>Raised on the completing request's context; UI consumers must dispatch to their thread.</summary>
    public event EventHandler? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource request;
        lock (gate)
        {
            if (disposed || !enabled) return;
            current = false;
            if (suspended)
            {
                discovery?.Cancel();
                return;
            }
            discovery?.Cancel();
            CancelResolutions();
            discovery = request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }
        try
        {
            var lenses = await client.RequestCodeLensesAsync(uri, request.Token);
            lock (gate)
            {
                if (disposed || request.IsCancellationRequested || discovery != request) return;
                entries = lenses.Select(l => new Entry(l)).ToArray();
                current = true;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            await ResolveVisibleAsync();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            lock (gate)
            {
                if (discovery == request) discovery = null;
                request.Dispose();
            }
        }
    }

    public Task SetViewportAsync(int firstLine, int lastLine)
    {
        lock (gate)
        {
            if (disposed || suspended || !enabled) return Task.CompletedTask;
            this.firstLine = firstLine;
            this.lastLine = lastLine;
            foreach (var entry in entries)
            {
                if (IsVisible(entry) || entry.Resolution is not { } pending) continue;
                entry.Resolution = null;
                entry.Attempted = false;
                pending.Cancel();
            }
        }
        return ResolveVisibleAsync();
    }

    private bool IsVisible(Entry entry) => firstLine >= 0 &&
        entry.Lens.Range.Start.Line >= firstLine && entry.Lens.Range.Start.Line <= lastLine;

    private Task ResolveVisibleAsync()
    {
        var work = new List<(Entry Entry, CancellationTokenSource Request)>();
        lock (gate)
        {
            if (disposed || suspended || !enabled) return Task.CompletedTask;
            foreach (var entry in entries)
            {
                if (!IsVisible(entry) || entry.Attempted || entry.Lens.Command is not null) continue;
                entry.Attempted = true;
                entry.Resolution = new CancellationTokenSource();
                work.Add((entry, entry.Resolution));
            }
        }
        return Task.WhenAll(work.Select(w => ResolveAsync(w.Entry, w.Request)));
    }

    private async Task ResolveAsync(Entry entry, CancellationTokenSource request)
    {
        try
        {
            var resolved = await client.ResolveCodeLensAsync(entry.Lens, request.Token);
            lock (gate)
            {
                if (disposed || request.IsCancellationRequested || entry.Resolution != request || resolved is null)
                    return;
                entry.Lens = resolved;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        finally
        {
            lock (gate)
            {
                if (entry.Resolution == request) entry.Resolution = null;
                request.Dispose();
            }
        }
    }

    private void CancelResolutions()
    {
        foreach (var entry in entries)
        {
            entry.Resolution?.Cancel();
            entry.Resolution = null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            client.CodeLensRefreshRequested -= OnRefreshRequested;
            discovery?.Cancel();
            CancelResolutions();
            entries = [];
        }
    }
}
