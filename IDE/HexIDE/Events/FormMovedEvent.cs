using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

/// <summary>
/// A form's file is now at its new <see cref="FormDefinition.AbsolutePath"/>, its contents as they were, so what knows the
/// document by its name, a code window or the language server, takes the new one.
/// </summary>
public record FormMovedEvent(FormDefinition Form) : IEvent;
