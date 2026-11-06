using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

/// <summary>A server's action, retaining its origin and opaque payload through resolution.</summary>
public sealed record OfferedCodeAction(string Server, string Title, string? DisabledReason, WorkspaceEdit? Edit,
    Command? Command, Func<CancellationToken, Task<OfferedCodeAction>>? Resolve = null,
    Func<Command, CancellationToken, Task>? Execute = null,
    Func<string, RenameDocumentSnapshot?>? DocumentFor = null,
    Func<Command, bool>? CanExecute = null,
    string? ServerDisplayName = null);
