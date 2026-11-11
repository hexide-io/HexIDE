using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

/// <summary>
/// A form's, UserControl's or PropertyPage's designer half became read-only, or stopped being, without a reload: as when the
/// add-in whose format writes it is taken out. What shows it, the designer and the code window, reads the verdict again.
/// </summary>
public record FormReadOnlyChangedEvent(FormDefinition Designer) : IEvent;
