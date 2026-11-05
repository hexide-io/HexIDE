using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>A rename's edits or the server's reason for refusing it. Null means no change required.</summary>
public sealed record RenameResult(WorkspaceEdit? Edit, string? Refusal,
    Func<string, RenameDocumentSnapshot?>? DocumentFor = null);

/// <summary>The answering connection's text and version at request time, and whether its sent version has since changed.</summary>
public sealed record RenameDocumentSnapshot(int Version, string Text, bool VersionChanged = false);
