using System.Collections.Concurrent;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Addins;

public sealed class AddinDiagnosticsService : IDiagnosticsAccess, IDisposable
{
    private readonly ILspClient _lspClient;
    private readonly IProjectManager _projectManager;
    // Keyed with the URI comparer, not by raw string: a server that normalises the URI it echoes
    // back would otherwise accumulate two entries for one document, and report its diagnostics twice.
    private readonly ConcurrentDictionary<string, Diagnostic[]> _latest = new(LspDocumentUri.Comparer);

    public AddinDiagnosticsService(ILspClient lspClient, IProjectManager projectManager)
    {
        _lspClient = lspClient;
        _projectManager = projectManager;
        lspClient.DiagnosticsPublished += OnPublished;
    }

    private void OnPublished(object? sender, PublishDiagnosticsParams p) =>
        _latest[p.Uri] = p.Diagnostics;

    public IReadOnlyList<AddinDiagnostic> GetAll() =>
        [.. _latest.SelectMany(kv => kv.Value.Select(d => Map(kv.Key, d)))];

    public IReadOnlyList<AddinDiagnostic> GetFor(string fileName) => GetFor(fileName, null);

    /// <inheritdoc/>
    /// <remarks>
    /// The document is found first and its diagnostics second, rather than the other way round. This used
    /// to compare the caller's name with the last slash-separated segment of every wire URI it held — which
    /// answers correctly only while a document's wire name happens to end in its own name. It does not for a
    /// document named by its file (<c>Utilities</c> saved as <c>util.bas</c>), and it cannot tell two
    /// projects' <c>Module1</c> apart at all.
    /// </remarks>
    public IReadOnlyList<AddinDiagnostic> GetFor(string fileName, string? project)
    {
        var found = DocumentLookup.Find(_projectManager.LoadedProjects, fileName, project);
        if (found.Count != 1) return [];

        var document = found[0];
        var wire = DocumentWireName.For(document);
        return [.. _latest
            .Where(kv => LspDocumentUri.AreSame(kv.Key, wire))
            .SelectMany(kv => kv.Value.Select(d => Map(document, d)))];
    }

    public void Dispose() => _lspClient.DiagnosticsPublished -= OnPublished;

    /// <summary>
    /// A diagnostic reported under a wire name, named for an add-in by the document it belongs to.
    /// </summary>
    /// <remarks>
    /// A name a loaded project does not answer to keeps the URI's last segment, which is all that can
    /// honestly be said about it: the document may belong to a project that has since been unloaded, or to
    /// none this IDE owns.
    /// </remarks>
    private AddinDiagnostic Map(string uri, Diagnostic d) =>
        DocumentFor(uri) is { } document
            ? Map(document, d)
            : new AddinDiagnostic(LastSegment(uri),
                d.Range.Start.Line + 1, d.Range.Start.Character + 1,
                d.Message, (AddinDiagnosticSeverity)(d.Severity ?? DiagnosticSeverity.Error),
                d.CodeText, d.Source);

    private static AddinDiagnostic Map(DocumentIdentity document, Diagnostic d) =>
        new(document.Name,
            d.Range.Start.Line + 1,
            d.Range.Start.Character + 1,
            d.Message,
            (AddinDiagnosticSeverity)(d.Severity ?? DiagnosticSeverity.Error),
            d.CodeText,
            d.Source,
            document.Project.Name);

    private DocumentIdentity? DocumentFor(string uri)
    {
        foreach (var project in _projectManager.LoadedProjects)
            foreach (var document in DocumentLookup.DocumentsOf(project))
                if (LspDocumentUri.AreSame(uri, DocumentWireName.For(document)))
                    return document;
        return null;
    }

    private static string LastSegment(string uri)
    {
        var slash = uri.LastIndexOf('/');
        return slash >= 0 ? uri[(slash + 1)..] : uri;
    }
}
