using HexIDE.IDE;
using HexIDE.Lsp.Messages;

namespace HexIDE.Events;

public sealed record CallHierarchyEvent(string Uri, Position Position) : IEvent;
