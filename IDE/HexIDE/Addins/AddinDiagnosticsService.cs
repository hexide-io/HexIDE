using System.Collections.Concurrent;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Addins;

public sealed class AddinDiagnosticsService : IDiagnosticsAccess, IDisposable
{
    private readonly ILspClient _lspClient;
    private readonly ConcurrentDictionary<string, Diagnostic[]> _latest = new();

    public AddinDiagnosticsService(ILspClient lspClient)
    {
        _lspClient = lspClient;
        lspClient.DiagnosticsPublished += OnPublished;
    }

    private void OnPublished(object? sender, PublishDiagnosticsParams p) =>
        _latest[p.Uri] = p.Diagnostics;

    public IReadOnlyList<AddinDiagnostic> GetAll() =>
        _latest.SelectMany(kv => kv.Value.Select(d => Map(kv.Key, d))).ToList();

    public IReadOnlyList<AddinDiagnostic> GetFor(string fileName) =>
        _latest
            .Where(kv => ExtractName(kv.Key).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Select(d => Map(kv.Key, d)))
            .ToList();

    public void Dispose() => _lspClient.DiagnosticsPublished -= OnPublished;

    private static AddinDiagnostic Map(string uri, Diagnostic d) =>
        new(ExtractName(uri),
            d.Range.Start.Line + 1,
            d.Range.Start.Character + 1,
            d.Message,
            (AddinDiagnosticSeverity)(d.Severity ?? DiagnosticSeverity.Error));

    private static string ExtractName(string uri)
    {
        var slash = uri.LastIndexOf('/');
        return slash >= 0 ? uri[(slash + 1)..] : uri;
    }
}
