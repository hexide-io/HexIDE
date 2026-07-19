using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class CreateOrNavigateToSubEvent : IEvent
{
    public CreateOrNavigateToSubEvent(FormDefinition form, string sub)
    {
        Form = form;
        Sub = sub;
    }

    public FormDefinition Form { get; }
    public string Sub { get; }
}