using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Events;

public class ModulePropertiesSelectedEvent : IEvent
{
    public ModulePropertiesSelectedEvent(ModuleDefinition? module)
    {
        Module = module;
    }

    public ModuleDefinition? Module { get; }
}
