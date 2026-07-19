using HexIDE.IDE;

namespace HexIDE.Events;

public class TextInputForPropertyEvent(string? text) : IEvent
{
    public string? Text { get; } = text;
}