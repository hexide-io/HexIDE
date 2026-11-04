using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

public sealed record ReferenceSearchError(string Server, string Message);
public sealed record ReferenceSearchResult(Location[] Locations, ReferenceSearchError[] Errors);
