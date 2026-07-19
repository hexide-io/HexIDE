using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class ActivateCodeEditorEvent : IEvent
{
    public ActivateCodeEditorEvent(FormDefinition form)
    {
        Form = form;
    }

    public ActivateCodeEditorEvent(ModuleDefinition module)
    {
        Module = module;
    }

    public FormDefinition? Form { get; }
    public ModuleDefinition? Module { get; }
    public bool Handled { get; set; }
}