using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public record ModuleUnloadedEvent(ModuleDefinition Module) : IEvent;
