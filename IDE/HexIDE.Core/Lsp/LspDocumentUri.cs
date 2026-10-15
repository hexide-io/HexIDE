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
    /// <summary>
    /// The <c>file:</c> URI naming a document that exists on disk.
    ///
    /// <para>
    /// Here rather than at the call site because it is the counterpart to <see cref="AreSame"/>: this
    /// class already owns what makes two URIs the same document, and construction is where that starts.
    /// </para>
    ///
    /// <para>
    /// <b>The extension has to survive.</b> Servers are matched to documents by it, so a URI that mangles
    /// or drops the extension is not merely ugly — it routes nowhere, and the failure is a document that
    /// opens with no language features and no error. Percent-encoding is what <see cref="Uri"/> does to a
    /// space or a <c>#</c> in a path, and the comparison unescapes before matching, so an encoded path and
    /// a literal one still name the same document.
    /// </para>
    ///
    /// <para>
    /// <paramref name="hostPath"/> must be a path in the <em>host</em> filesystem's own separators. A path
    /// that came out of a <c>.vbp</c> is backslash-separated on every platform and must be converted
    /// first, or on Linux the backslashes become part of the filename — the trap the project files have
    /// their own helpers for.
    /// </para>
    /// </summary>
    public static string ForFile(string hostPath) => new Uri(Path.GetFullPath(hostPath)).AbsoluteUri;

    /// <summary>
    /// The <c>untitled:</c> URI naming a document that has no file yet: <c>untitled:&lt;project&gt;/&lt;name&gt;&lt;ext&gt;</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no leading slash</b>, and both spellings normalise differently, so one is chosen here and
    /// never mixed. The extension is what routing reads, so it is appended outside the escaping — it is
    /// already a literal drawn from a closed set, and escaping the dot would route it nowhere.
    /// </para>
    /// <para>
    /// <b>Each segment is escaped, not interpolated raw.</b> texlab silently drops a notification whose URI
    /// is not strictly valid — no response, no error, nothing on standard error, and the connection stays up
    /// and answers about later documents normally, so a request naming that document is simply never
    /// answered (hexide-io/HexIDE#486). The same server in the same process answers the percent-encoded
    /// form. That is the whole argument for escaping, and it was measured rather than assumed.
    /// </para>
    /// </remarks>
    public static string ForUntitled(string projectName, string documentName, string extension) =>
        $"untitled:{Uri.EscapeDataString(projectName)}/{Uri.EscapeDataString(documentName)}{extension}";

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

        // untitled:<Project>/<Name>.<ext> — both segments are VB6 names, and VB6 names are
        // case-insensitive. Inherited from the vb6:// scheme this replaced, for the same reason.
        //
        // Only ever exercised against a server that speaks the name back. A PUSH server echoes it byte
        // for byte, in either spelling (measured on texlab, raw and percent-encoded); a PULL server
        // cannot echo anything, because a DocumentDiagnosticReport carries no URI, so the client files
        // the answer under the name it asked about. Worth saying because the obvious test — compare the
        // published URI with the one sent — is a tautology on three of the five foreign servers.
        "untitled" => true,

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
