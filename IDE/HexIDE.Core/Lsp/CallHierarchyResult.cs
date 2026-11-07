using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

public sealed record CallHierarchyRoot(ILspClient Client, CallHierarchyItem Item, string Server = "");
public sealed record CallHierarchyResult(CallHierarchyRoot[] Roots, ReferenceSearchError[] Errors);
