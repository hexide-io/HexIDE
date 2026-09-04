using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public interface IEditorService
{
    void EditForm(FormDefinition? form);
    void EditCode(FormDefinition? form);
    void EditCode(ModuleDefinition? module);

    /// <summary>Opens a file the project carries but does not compile, in the plain-text editor.</summary>
    void EditRelatedDocument(RelatedDocumentDefinition? relatedDocument);
}