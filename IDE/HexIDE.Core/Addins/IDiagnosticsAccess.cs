namespace HexIDE.Addins;

public interface IDiagnosticsAccess
{
    IReadOnlyList<AddinDiagnostic> GetAll();

    /// <inheritdoc cref="GetFor(string, string?)"/>
    /// <remarks>
    /// Kept for add-ins written before a document could be named by its project. It searches every loaded
    /// project, and answers empty when the name is ambiguous rather than merging two documents' diagnostics.
    /// </remarks>
    IReadOnlyList<AddinDiagnostic> GetFor(string fileName);

    /// <summary>
    /// The diagnostics currently held for one document.
    /// </summary>
    /// <param name="fileName">The document's VB6 name, in any case.</param>
    /// <param name="project">
    /// The project to look in, by name, or null to search every loaded one.
    /// </param>
    IReadOnlyList<AddinDiagnostic> GetFor(string fileName, string? project);
}
