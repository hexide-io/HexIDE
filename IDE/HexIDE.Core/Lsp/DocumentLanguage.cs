namespace HexIDE.Lsp;

/// <summary>
/// Works out what a document URI claims to be, so a request can be routed to the servers that want it.
///
/// <para>
/// <b>This no longer decides what language a document is.</b> It used to own a global
/// extension-to-language table, which quietly assumed every server would agree about what an extension
/// means. Two servers can legitimately disagree — one calls a file <c>python</c>, another <c>python3</c> —
/// and a single table forces a winner, leaving the loser wrong about every file it sees. So the mapping
/// moved to the servers: each declares the extensions it claims and what it wants those documents called,
/// and this class only extracts the two things routing needs to compare against.
/// </para>
///
/// <para>
/// <b>Scheme first, extension second.</b> HexIDE's own documents are <c>vb6://module/Module1</c> and
/// <c>vb6://form/Form1</c> — they carry no extension at all, so an extension-only rule would fail to
/// classify the only documents the IDE opens today. That rule stays here rather than moving to a server's
/// declaration, because the scheme is HexIDE's own invention and not a server's claim to make.
/// </para>
/// </summary>
public static class DocumentLanguage
{
    /// <summary>
    /// HexIDE's own scheme, and the language identifier a server must declare to be offered the documents
    /// carrying it.
    /// </summary>
    public const string Vb6 = "vb6";

    /// <summary>
    /// The language a URI's scheme names, or null when the scheme names a transport rather than a language.
    ///
    /// <para>
    /// Only <c>vb6</c> qualifies today, because it is the only scheme HexIDE mints. <c>file</c> deliberately
    /// does not: it says where a document is, not what it is.
    /// </para>
    ///
    /// <para>
    /// A server is offered these documents by declaring this as its language identifier. That is a real
    /// coupling worth naming: a replacement VB6 server that calls the language something else would not be
    /// offered the IDE's own documents. It is the correct trade while the scheme is HexIDE's own — the
    /// alternative is letting configuration claim a scheme, which invites two servers to disagree about
    /// what <c>vb6://</c> means, and unlike a file extension there is no outside authority to appeal to.
    /// </para>
    /// </summary>
    public static string? SchemeLanguageOf(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0) return null;

        var scheme = uri[..schemeEnd];
        return scheme.Equals(Vb6, StringComparison.OrdinalIgnoreCase) ? Vb6 : null;
    }

    /// <summary>
    /// The document's extension including its leading dot, lower-cased, or null when it has none.
    ///
    /// <para>
    /// Null is an ordinary answer. A document nothing claims opens with language features absent, which is
    /// correct rather than a failure.
    /// </para>
    /// </summary>
    public static string? ExtensionOf(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;

        var lastDot = uri.LastIndexOf('.');
        var lastSlash = uri.LastIndexOf('/');
        if (lastDot < 0 || lastDot < lastSlash) return null;

        // Trim anything a URI may carry after the path, so "…/a.bas?v=2" still classifies.
        var tail = uri[lastDot..];
        var cut = tail.IndexOfAny(['?', '#']);
        if (cut >= 0) tail = tail[..cut];

        return tail.Length > 1 ? tail.ToLowerInvariant() : null;
    }

    /// <summary>
    /// The VB6 source extensions, as the bundled server declares them.
    ///
    /// <para>
    /// Here rather than in configuration only because the bundled entry is built in code; it is an ordinary
    /// claim by an ordinary server, and a user's entry may claim the same extensions or none of them.
    /// </para>
    /// </summary>
    public static readonly string[] Vb6Extensions =
        [".bas", ".cls", ".frm", ".ctl", ".pag", ".dob", ".dsr"];
}
