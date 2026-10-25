using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class DocumentNameChangedEvent : IEvent
{
    public DocumentNameChangedEvent(DocumentIdentity document)
    {
        Document = document;
    }

    public DocumentIdentity Document { get; }
}
