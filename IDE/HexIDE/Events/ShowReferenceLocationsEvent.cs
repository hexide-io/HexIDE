using HexIDE.IDE;
using HexIDE.Lsp.Messages;

namespace HexIDE.Events;

public sealed record ShowReferenceLocationsEvent(Location[] Locations) : IEvent;
