using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class ActivateFormEditorEvent : IEvent
{
    public ActivateFormEditorEvent(FormDefinition form)
    {
        Form = form;
    }

    public FormDefinition Form { get; }
    public bool Handled { get; set; }
}