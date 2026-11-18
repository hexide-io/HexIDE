using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class CreateOrNavigateToSubEvent : IEvent
{
    public CreateOrNavigateToSubEvent(FormDefinition form, string sub, string objectName, string eventName)
    {
        Form = form;
        Sub = sub;
        ObjectName = objectName;
        EventName = eventName;
    }

    public FormDefinition Form { get; }
    public string Sub { get; }
    public string ObjectName { get; }
    public string EventName { get; }
}

public class NavigateToDeclarationsEvent(FormDefinition form) : IEvent
{
    public FormDefinition Form { get; } = form;
}
