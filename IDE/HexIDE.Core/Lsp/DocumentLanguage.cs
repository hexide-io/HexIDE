namespace HexIDE.Lsp;

/// <summary>
/// Works out which language a document URI names, so a request can be routed to the servers that claim it.
///
/// <para>
/// <b>Scheme first, extension second.</b> HexIDE's own documents are <c>vb6://module/Module1</c> and
/// <c>vb6://form/Form1</c> — they carry no extension at all, so an extension-only rule would fail to
/// classify the only language currently served. A scheme that names a language is also more reliable than a
/// filename when both are present.
/// </para>
///
/// <para>
/// The language identifier is the protocol's own concept — it travels in <c>didOpen</c> and is what servers
/// key on — rather than anything drawn from the project file. A member's kind in a <c>.vbp</c> is a
/// project-file concept, and the documents most likely to need a second server are exactly the ones carried
/// alongside a project rather than compiled by it, which have no kind at all.
/// </para>
/// </summary>
public static class DocumentLanguage
{
    public const string Vb6 = "vb6";

    // Extensions are the fallback path, for documents that arrive as file:// URIs. Deliberately small: this
    // is not a registry of every language HexIDE might one day open, only the mapping needed to route what
    // it can open today. A server contributing its own languages is what grows this later.
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bas"] = Vb6,
        [".cls"] = Vb6,
        [".frm"] = Vb6,
        [".ctl"] = Vb6,
        [".pag"] = Vb6,
        [".dob"] = Vb6,
        [".dsr"] = Vb6,
    };

    /// <summary>
    /// The language identifier for a document URI, or null when nothing claims to recognise it. Null is a
    /// normal answer, not a failure: an unrecognised document opens with language features absent.
    /// </summary>
    public static string? Of(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        // A scheme that names a language wins. `vb6://module/Module1` has no extension to fall back to.
        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd > 0)
        {
            var scheme = uri[..schemeEnd];
            if (scheme.Equals(Vb6, StringComparison.OrdinalIgnoreCase)) return Vb6;
            // `file` names a transport, not a language — fall through to the extension.
            if (!scheme.Equals("file", StringComparison.OrdinalIgnoreCase)) return null;
        }

        var lastDot = uri.LastIndexOf('.');
        var lastSlash = uri.LastIndexOf('/');
        if (lastDot < 0 || lastDot < lastSlash) return null;

        // Trim anything a URI may carry after the path, so "…/a.bas?v=2" still classifies.
        var tail = uri[lastDot..];
        var cut = tail.IndexOfAny(['?', '#']);
        if (cut >= 0) tail = tail[..cut];

        return ByExtension.GetValueOrDefault(tail);
    }
}
