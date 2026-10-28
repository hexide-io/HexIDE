using HexIDE.IDE;

namespace HexIDE.Events;

// this is pretty bad design, but editors lazily save back to FormDefinition now. TODO
public class ApplyAllUnsavedChangesEvent : IEvent
{
    /// <summary>
    /// Flush only an editor or designer that has run ahead of its model, and leave the rest alone.
    /// </summary>
    /// <remarks>
    /// Crash recovery asks this every few seconds (hexide-io/HexIDE#499). An unconditional flush rebuilds every open
    /// designer's component list and announces its form's name each time, which the Object Browser and every tab title
    /// answer, for nothing. A flush where nothing is ahead changes nothing, so skipping it loses nothing.
    /// </remarks>
    public bool OnlyWhereAhead { get; init; }
}