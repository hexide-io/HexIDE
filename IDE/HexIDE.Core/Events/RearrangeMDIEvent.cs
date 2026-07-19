using HexIDE.IDE;

namespace HexIDE.Events;

public class RearrangeMDIEvent : IEvent
{
    public RearrangeMDIEvent(MDIRearrangeKind kind)
    {
        Kind = kind;
    }

    public MDIRearrangeKind Kind { get; }
}

public enum MDIRearrangeKind
{
    TileHorizontally,
    TileVertically,
    Cascade,
    ArrangeIcons
}