using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public interface IEditorService
{
    void EditForm(FormDefinition? form);
    void EditCode(FormDefinition? form);
    void EditCode(ModuleDefinition? module);
}