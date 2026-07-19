using HexIDE.IDE;

namespace HexIDE.Events;

public sealed class FileClosedEvent(string title) : IEvent
{
    public string Title { get; } = title;
}
