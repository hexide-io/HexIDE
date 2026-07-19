using System.Collections.Concurrent;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Desktop.Server;

internal sealed class DiagnosticsCache : IDisposable
{
    private readonly ILspClient _client;
    private readonly ConcurrentDictionary<string, PublishDiagnosticsParams> _latest = new();

    public DiagnosticsCache(ILspClient client)
    {
        _client = client;
        _client.DiagnosticsPublished += OnPublished;
    }

    private void OnPublished(object? sender, PublishDiagnosticsParams p) => _latest[p.Uri] = p;

    public IReadOnlyCollection<PublishDiagnosticsParams> GetAll() => _latest.Values.ToArray();

    public void Dispose() => _client.DiagnosticsPublished -= OnPublished;
}
