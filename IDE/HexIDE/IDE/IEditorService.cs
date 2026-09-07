using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

public interface IEditorService
{
    void EditForm(FormDefinition? form);
    void EditCode(FormDefinition? form);
    void EditCode(ModuleDefinition? module);

    /// <summary>Opens a file the project carries but does not compile, in the plain-text editor.</summary>
    void EditRelatedDocument(RelatedDocumentDefinition? relatedDocument);

    /// <summary>
    /// Opens whatever document a URI names and puts the caret at a position in it. False when nothing
    /// loaded answers to that URI.
    /// </summary>
    /// <remarks>
    /// <b>This is the inbound half of go-to-definition, and it is a whole half.</b> A language server
    /// answers with a <c>Location</c> — a URI and a range — and that URI may name a document the editor
    /// never opened and the server was never told about. Without a way back from a URI to a document,
    /// every cross-file answer a server gives is unusable, however correct it is.
    ///
    /// <para>
    /// <paramref name="line"/> and <paramref name="column"/> are ONE-BASED, matching AvaloniaEdit, not
    /// the protocol's zero-based <c>Position</c>. Converting at the call site keeps this method usable by
    /// callers that have nothing to do with LSP.
    /// </para>
    /// </remarks>
    bool NavigateTo(string uri, int line, int column);
}