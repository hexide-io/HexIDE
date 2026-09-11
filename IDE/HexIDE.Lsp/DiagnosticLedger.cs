using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>
/// What each source of diagnostics currently says about each document, so that one source going quiet
/// erases only its own marks.
///
/// <para>
/// <b>The protocol has no notion of ownership, and that is exactly the gap this fills.</b>
/// <c>textDocument/publishDiagnostics</c> is a whole-document replacement — every consumer here
/// implements it that way, because a server that has fixed nothing must still be able to say "these three
/// errors are now two". But HexIDE feeds more than one source into that single channel: the language
/// server or servers claiming a document, and the real VB6 compiler injecting what only a compiler can
/// know. Replacement then means the last writer wins, so a build clearing its own previous errors deleted
/// whatever a server had published for the same form, and the marks only came back on the next keystroke
/// (hexide-io/HexIDE#358).
/// </para>
///
/// <para>
/// Keeping the last set per <c>(document, source)</c> and raising the union restores replacement to the
/// scope it belongs at: a source replaces its own rows, and the document's diagnostics are everything
/// currently claimed about it. Consumers are unchanged — what reaches them is still one whole-document
/// set — which is what keeps the marker pipeline, the addin cache and the automation server unable to
/// disagree about which errors are current.
/// </para>
/// </summary>
internal sealed class DiagnosticLedger
{
    private readonly Lock _gate = new();

    // Keyed with the URI comparer rather than by raw string: a server is under no obligation to echo a URI
    // back byte-for-byte, and two spellings of one document must merge rather than accumulate. See
    // LspDocumentUri.
    private readonly Dictionary<string, Document> _documents = new(LspDocumentUri.Comparer);

    /// <summary>
    /// Records what one source now says about one document, and returns the document's whole set —
    /// everything every source currently claims about it, ready to raise.
    /// </summary>
    public PublishDiagnosticsParams Record(string uri, string owner, Diagnostic[] diagnostics)
    {
        lock (_gate)
        {
            if (!_documents.TryGetValue(uri, out var document))
                _documents[uri] = document = new Document(uri);

            document.Set(owner, diagnostics);
            var published = document.Publish();

            // A document nothing claims any more is dropped, so the ledger holds live marks rather than
            // every URI ever mentioned — the clean case, which is most of them, costs nothing to remember.
            if (published.Diagnostics.Length == 0) _documents.Remove(uri);

            return published;
        }
    }

    /// <summary>
    /// Drops everything one source has published, anywhere, and returns what each affected document now
    /// says.
    ///
    /// <para>
    /// <b>Driven by what the source published, not by what the caller believes it published.</b> That
    /// distinction is the point: the compiler's clear used to iterate the project's <em>current</em> forms,
    /// so a form renamed since the last build was never cleared and kept a stale compiler marker — the
    /// other half of #269. A source cannot lose track of its own rows this way.
    /// </para>
    /// </summary>
    public IReadOnlyList<PublishDiagnosticsParams> Withdraw(string owner)
    {
        lock (_gate)
        {
            List<PublishDiagnosticsParams> published = [];
            foreach (var document in _documents.Values)
            {
                if (!document.Remove(owner)) continue;
                published.Add(document.Publish());
            }

            // A document nothing claims any more is dropped, so the ledger tracks live marks rather than
            // every URI ever mentioned.
            foreach (var p in published)
            {
                if (p.Diagnostics.Length == 0) _documents.Remove(p.Uri);
            }

            return published;
        }
    }

    /// <summary>One document's sources, in the order they first spoke about it.</summary>
    private sealed class Document(string uri)
    {
        // A list rather than a dictionary: the raised order must be stable, or a consumer rendering them
        // in order shuffles its marks whenever an unrelated source republishes. Sources per document are
        // counted on one hand.
        private readonly List<(string Owner, Diagnostic[] Diagnostics)> _rows = [];

        /// <summary>
        /// The spelling this document was first seen under, and the one every union is raised with.
        ///
        /// <para>
        /// Deliberately not the spelling of the publish that triggered it. Two sources naming one document
        /// differently — the compiler's <c>vb6://form/Form1</c> against a server's echoed
        /// <c>vb6://form/form1</c> — would otherwise raise the same merged set under two URIs, and a
        /// consumer keyed by raw string would report it twice. Editors compare with
        /// <see cref="LspDocumentUri.AreSame"/>, so a stable spelling costs them nothing.
        /// </para>
        /// </summary>
        private readonly string _uri = uri;

        public void Set(string owner, Diagnostic[] diagnostics)
        {
            var at = _rows.FindIndex(r => r.Owner == owner);

            // An empty set from a source means that source has nothing to say, which is not the same as
            // that source never having spoken — dropping the row is what stops the ledger growing a
            // permanent entry per source per document.
            if (diagnostics.Length == 0)
            {
                if (at >= 0) _rows.RemoveAt(at);
                return;
            }

            if (at >= 0) _rows[at] = (owner, diagnostics);
            else _rows.Add((owner, diagnostics));
        }

        public bool Remove(string owner)
        {
            var at = _rows.FindIndex(r => r.Owner == owner);
            if (at < 0) return false;
            _rows.RemoveAt(at);
            return true;
        }

        public PublishDiagnosticsParams Publish() =>
            new(_uri, _rows.SelectMany(r => r.Diagnostics).ToArray());
    }
}
