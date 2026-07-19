using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class FormUnloadedEvent : IEvent
{
    public FormDefinition Form { get; }

    public FormUnloadedEvent(FormDefinition form)
    {
        Form = form;
    }
}