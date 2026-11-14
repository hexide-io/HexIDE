using HexIDE.IDE;

namespace HexIDE.Events;

public sealed record ProjectFileSavedEvent(object Owner) : IEvent;
