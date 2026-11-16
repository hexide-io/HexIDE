using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

public sealed record TypeHierarchyRoot(ILspClient Client, TypeHierarchyItem Item, string Server = "");
public sealed record TypeHierarchyResult(TypeHierarchyRoot[] Roots, ReferenceSearchError[] Errors);
