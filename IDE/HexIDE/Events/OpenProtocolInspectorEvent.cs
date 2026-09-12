using HexIDE.IDE;

namespace HexIDE.Events;

/// <summary>
/// Asks for the protocol inspector, optionally already filtered to one server.
/// </summary>
/// <remarks>
/// <b>An event rather than a reference, because the connection list must not know the shell.</b> The
/// inspector lives in the document dock and only <c>MainViewViewModel</c> owns that; handing the connection
/// list a reference to the shell to open one tab would invert the dependency for a single gesture. This is
/// the shape <c>OpenTranslationEditorEvent</c> already set.
/// </remarks>
/// <param name="ConnectionId">
/// The server to filter to, or null for the whole interleaved timeline. Carried here rather than left to
/// the caller to set afterwards, so the window is never briefly shown unfiltered on its way to the answer.
/// </param>
public sealed class OpenProtocolInspectorEvent(string? connectionId = null) : IEvent
{
    public string? ConnectionId { get; } = connectionId;
}
