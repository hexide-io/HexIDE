using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>A rename's edits or the server's reason for refusing it. Null means no change required.</summary>
public sealed record RenameResult(WorkspaceEdit? Edit, string? Refusal);
