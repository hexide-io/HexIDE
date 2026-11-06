using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

/// <summary>
/// A module's file is now at its new <see cref="ModuleDefinition.AbsolutePath"/>, its code as it was, so what knows the
/// document by its name, a code window or the language server, takes the new one.
/// </summary>
public record ModuleMovedEvent(ModuleDefinition Module) : IEvent;
