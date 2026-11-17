using HexIDE.IDE;
using HexIDE.Lsp.Messages;

namespace HexIDE.Events;

public sealed record TypeHierarchyEvent(string Uri, Position Position) : IEvent;
