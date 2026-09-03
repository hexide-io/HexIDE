namespace HexIDE.Lsp;

/// <summary>
/// Compares LSP document URIs for identity.
///
/// <para>
/// A language server is under no obligation to echo a URI back byte-for-byte, and conformant ones
/// routinely do not — normalising the Windows drive letter, or percent-encoding a character the
/// client left literal. Comparing with <c>==</c> therefore drops that server's diagnostics **silently**:
/// no error, no log, the feature simply never appears. Measured against a real third-party server,
/// which answered <c>file:///c:/…</c> to our <c>file:///C:/…</c> (see #236).
/// </para>
///
/// <para>
/// <b><c>OrdinalIgnoreCase</c> is not the fix.</b> It would match two genuinely different files on a
/// case-sensitive filesystem, trading a silent drop for a silent mis-attribution — the worse of the
/// two. What is wanted is RFC 3986 comparison with the scheme-specific path rules layered on top.
/// </para>
/// </summary>
public static class LspDocumentUri
{
    /// <summary>True when both URIs identify the same document.</summary>
    public static bool AreSame(string? a, string? b)
    {
        if (a is null || b is null)
            return ReferenceEquals(a, b);
        // Fast path, and the overwhelmingly common one: our own server echoes URIs verbatim.
        return string.Equals(a, b, StringComparison.Ordinal) || Normalize(a) == Normalize(b);
    }

    /// <summary>
    /// An equality comparer for use where URIs are dictionary keys. Without it, one document reached
    /// through two spellings becomes two entries — which shows up as duplicated diagnostics rather
    /// than missing ones, so it is quieter than <see cref="AreSame"/>'s failure and no less wrong.
    /// </summary>
    public static IEqualityComparer<string> Comparer { get; } = new UriComparer();

    /// <summary>
    /// Canonical form for comparison — never for display, and never sent back on the wire. A server
    /// keys its own state by the string it sent, so echoing a normalised form back at it would break
    /// the very servers this exists to support.
    /// </summary>
    private static string Normalize(string uri)
    {
        // An unparseable URI is compared as-is rather than rejected: a server that sends something
        // odd should lose URI-matching precision, not have its diagnostics thrown away.
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return uri;

        // RFC 3986 §6.2.2.1: scheme and host are case-insensitive. The path is scheme-dependent.
        var scheme = parsed.Scheme.ToLowerInvariant();
        var host = parsed.Host.ToLowerInvariant();
        var path = parsed.GetComponents(UriComponents.Path, UriFormat.Unescaped);

        if (PathIsCaseInsensitive(scheme))
            path = path.ToLowerInvariant();

        return $"{scheme}://{host}/{path}";
    }

    private static bool PathIsCaseInsensitive(string lowercaseScheme) => lowercaseScheme switch
    {
        // Windows filesystems are case-insensitive, and the drive letter is the case that actually
        // bites. Deliberately NOT extended to macOS: its default volume is case-insensitive but it
        // can be formatted case-sensitive, and guessing wrong here mis-attributes a diagnostic to
        // the wrong file — worse than the missing-diagnostic bug this class fixes.
        "file" => OperatingSystem.IsWindows(),

        // HexIDE's own scheme (vb6://module/{name}, vb6://form/{name}). The path segment is a VB6
        // identifier, and VB6 identifiers are case-insensitive.
        "vb6" => true,

        // Everything else: RFC 3986 says the path is case-sensitive unless a scheme says otherwise,
        // and we do not know this scheme.
        _ => false,
    };

    private sealed class UriComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => AreSame(x, y);

        // Must hash the normalised form, or two URIs that AreSame lands in different buckets and the
        // comparer silently stops working for exactly the inputs it exists to handle.
        public int GetHashCode(string obj) => Normalize(obj).GetHashCode(StringComparison.Ordinal);
    }
}
