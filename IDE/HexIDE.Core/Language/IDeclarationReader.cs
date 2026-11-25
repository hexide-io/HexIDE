using HexIDE.Lsp.Messages;

namespace HexIDE.Language;

/// <summary>
/// The procedures and types a module declares, for the Object Browser and the Watch dialog to list when no language server
/// answers (#5403). It collects declarations: it doesn't bind a name to anything.
/// </summary>
public interface IDeclarationReader
{
    /// <summary>
    /// What <paramref name="source"/> declares, in order, each with the range from its header to its end and the range of its
    /// name. Both branches of an <c>#If</c> are read, so a declaration in either is listed.
    /// </summary>
    IReadOnlyList<DocumentSymbol> Declarations(string source);
}
