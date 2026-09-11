namespace HexIDE.Lsp;

/// <summary>
/// Names for the sources that publish onto the one diagnostics channel.
///
/// <para>
/// An owner is a <em>publisher</em>, not a category: it answers "who put this here", so that the same
/// party can later take it back without disturbing anything else on the document. Two sources sharing an
/// owner key would clear each other's marks, which is the defect the key exists to prevent.
/// </para>
///
/// <para>
/// Distinct from <see cref="Messages.Diagnostic.Source"/>, which is what a diagnostic says about itself
/// for the reader's benefit and which a server chooses freely. The compiler's owner key and the
/// <c>source</c> it stamps happen to read the same, and that is a convenience rather than a rule — nothing
/// derives one from the other, because a server is free to put anything in <c>source</c>, including
/// nothing.
/// </para>
/// </summary>
public static class DiagnosticOwner
{
    /// <summary>
    /// The real VB6 toolchain, injecting what only a compiler knows. Constant across builds by design:
    /// each build replaces the previous build's marks, which is precisely what one owner key means.
    /// </summary>
    public const string Vb6Compiler = "VB6 Compiler";

    /// <summary>
    /// The language server on the other end of a single connection. A connection has exactly one server,
    /// so one key suffices; the router gives each of its connections a key of its own, because there
    /// plurality is the point.
    /// </summary>
    public const string LanguageServer = "language server";
}
