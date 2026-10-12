namespace HexIDE.Lsp;

/// <summary>
/// The version a document is opened at: the one number the client and a document's session have to agree
/// on, because each sends half of one sequence.
/// </summary>
/// <remarks>
/// <para>
/// LSP has a document's version rise with every change, starting from the version its <c>didOpen</c>
/// carried. The client sends the open and the session sends the changes, so each has to count from where
/// the other left off. When the session counted from 0, its first change repeated the open's 1, and a server
/// that orders what it receives by version may discard a change that is not later than what it holds. Such a
/// server exists, and lost a single edit to it until the next one replaced it (hexide-io/HexIDE#470).
/// </para>
/// <para>
/// <b>Named once rather than written twice</b>, because that is how the two came apart: the open's 1 was a
/// literal in the client, and nothing at the session's end said what it had to follow.
/// </para>
/// </remarks>
public static class LspDocumentVersion
{
    /// <summary>
    /// The version a document's first <c>textDocument/didOpen</c> carries. A document's first change carries
    /// the next one. A reconnect's replay opens a document at the version it has reached instead.
    /// </summary>
    public const int Opening = 1;
}
