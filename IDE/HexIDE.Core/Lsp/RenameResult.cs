using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>A rename's edits or the server's reason for refusing it. Null means no change required.</summary>
public sealed record RenameResult(WorkspaceEdit? Edit, string? Refusal,
    Func<string, RenameDocumentSnapshot?>? DocumentFor = null);

/// <summary>The last text and version written to the answering connection, not the registry's copy.</summary>
public sealed record RenameDocumentSnapshot(int Version, string Text);
