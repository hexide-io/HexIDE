using HexIDE.IDE;

namespace HexIDE.Events;

public sealed class FileOpenedEvent(string title) : IEvent
{
    public string Title { get; } = title;
}
