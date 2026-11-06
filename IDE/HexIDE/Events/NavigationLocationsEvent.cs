using HexIDE.Lsp.Messages;

namespace HexIDE.Events;

public sealed record NavigationLocationsEvent(Location[] Locations) : IEvent;
