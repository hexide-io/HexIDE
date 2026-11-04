namespace HexIDE.Lsp;

/// <summary>Preparation is optional; a participating server can refuse or supply the prompt text/range.</summary>
public sealed record PrepareRenameResult(bool Allowed, HexIDE.Lsp.Messages.Range? Range = null, string? Placeholder = null,
    string? Refusal = null);
