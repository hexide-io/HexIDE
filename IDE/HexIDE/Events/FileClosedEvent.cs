using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

/// <param name="title">
/// The tab's title. Localized and project-prefixed, so it is a label rather than a name: it is here because
/// a tab that is not a VB6 document has nothing better.
/// </param>
/// <param name="document">
/// Which document the tab holds, when it holds one. Null for the Object Browser, a carried text file, the
/// protocol inspector and anything else that is a tab without being a form, module or class.
/// </param>
public sealed class FileClosedEvent(string title, DocumentIdentity? document = null) : IEvent
{
    public string Title { get; } = title;

    public DocumentIdentity? Document { get; } = document;
}
