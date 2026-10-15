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
/// <b>By extension, for every document.</b> This used to read a URI scheme first, because HexIDE named its
/// own documents <c>vb6://module/Module1</c> and those carry no extension to match on. Since #273 they are
/// named <c>file:</c> or <c>untitled:&lt;Project&gt;/&lt;Name&gt;.&lt;ext&gt;</c> and always carry one, so
/// there is a single rule again — which is the point of having stopped inventing a scheme.
/// </para>
///
/// <para>
/// What the extension cannot say is whether a document belongs to a VB6 project, and <c>.cls</c> is a VB6
/// class module and equally a LaTeX class file. That question is answered by
/// <see cref="EstablishesVb6"/>, applied to documents the caller states are project members.
/// </para>
/// </summary>
public static class DocumentLanguage
{
    /// <summary>
    /// The language identifier for VB6 — what the bundled server declares, what a user's entry may declare
    /// to establish that it serves VB6, and what documents are labelled as on the wire.
    ///
    /// <para>
    /// It no longer routes anything on its own. It was also a URI scheme until #273, and reading it as a
    /// claim meant a VB6 server attached as <c>vba</c> — at least as natural a choice — started,
    /// initialized and was never sent a document (hexide-io/HexIDE#277).
    /// </para>
    /// </summary>
    public const string Vb6 = "vb6";

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

    /// <summary>
    /// The VB6 extensions that mean VB6 and nothing else — how an entry establishes that it serves VB6,
    /// and so may be offered a project's class modules as well as its unambiguous documents.
    ///
    /// <para>
    /// <c>.cls</c> is deliberately absent. It is a VB6 class module and it is equally a LaTeX class file
    /// (hexide-io/HexIDE#279), so reading a lone <c>.cls</c> claim as "serves VB6" would hand a LaTeX
    /// server every module in the developer's project — a worse failure than the one this exists to fix.
    /// Nothing is lost by requiring more: a real VB6 server claims <c>.bas</c> and <c>.frm</c> too, and
    /// one that somehow serves only class modules can still say so with its language identifier.
    /// </para>
    /// </summary>
    public static readonly string[] UnambiguousVb6Extensions =
        [".bas", ".frm", ".ctl", ".pag", ".dob", ".dsr"];

    /// <summary>
    /// Whether a server's own claim establishes that it serves VB6 — it claims a VB6 extension no other
    /// language uses, or it declares the identifier outright.
    /// </summary>
    /// <param name="extensions">the extensions the entry claims.</param>
    /// <param name="languageId">the identifier the entry declares.</param>
    /// <remarks>
    /// <para>
    /// This is the gate a <b>project member</b> passes through, and only a project member: a carried
    /// <c>.cls</c> is an ordinary file and goes wherever its extension leads. A member is known to be VB6
    /// whatever its extension shares with another language, so offering it to a server that has not
    /// established VB6 would hand a LaTeX server the developer's source (hexide-io/HexIDE#279).
    /// </para>
    /// <para>
    /// <b>Applying it to every member is the same rule as applying it only to ambiguous extensions</b>,
    /// which is why there is no second predicate. On <c>.bas</c>, <c>.frm</c>, <c>.ctl</c> or <c>.pag</c>
    /// the very extension that matched is itself unambiguous, so an entry that routed on it satisfies this
    /// too and the gate is the identity. <c>.cls</c> is the only VB6 source extension it can actually
    /// exclude, and excluding it is the whole point.
    /// </para>
    /// <para>
    /// Said loosely as "an extension no other language uses", the rule would admit a Markdown server
    /// through <c>.md</c>. It is deliberately about <em>VB6's</em> unambiguous extensions.
    /// </para>
    /// </remarks>
    public static bool EstablishesVb6(IEnumerable<string> extensions, string? languageId) =>
        Vb6.Equals(languageId, StringComparison.OrdinalIgnoreCase)
        || extensions.Intersect(UnambiguousVb6Extensions, StringComparer.OrdinalIgnoreCase).Any();
}
