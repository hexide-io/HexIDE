using HexIDE.IDE;
using HexIDE.Lsp.Messages;

namespace HexIDE.Events;

public sealed record FindReferencesEvent(string Uri, Position Position) : IEvent;
