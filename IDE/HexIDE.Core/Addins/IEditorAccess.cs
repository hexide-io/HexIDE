namespace HexIDE.Addins;

public interface IEditorAccess
{
    AddinDocument? GetActiveDocument();
    AddinSelection? GetSelection();

    /// <inheritdoc cref="NavigateTo(string, int, int, string?)"/>
    /// <remarks>
    /// Kept for add-ins written before a document could be named by its project. It searches every loaded
    /// project and does nothing at all when the name is unknown or names more than one document; the
    /// overload below says which of those happened.
    /// </remarks>
    void NavigateTo(string fileName, int line, int column);

    /// <inheritdoc cref="SetContent(string, string, string?)"/>
    Task SetContent(string fileName, string content);

    /// <inheritdoc cref="ApplyEdits(string, IReadOnlyList{AddinTextEdit}, string?)"/>
    Task ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits);

    /// <summary>
    /// Puts the caret on a document, opening its editor if it is not already open.
    /// </summary>
    /// <param name="fileName">The document's VB6 name, in any case.</param>
    /// <param name="line">1-based.</param>
    /// <param name="column">1-based.</param>
    /// <param name="project">
    /// The project to look in, by name, or null to search every loaded one. A bare name that answers to
    /// documents in two loaded projects is refused rather than guessed at.
    /// </param>
    /// <returns>False when no such document was found, or when the name was ambiguous.</returns>
    bool NavigateTo(string fileName, int line, int column, string? project);

    /// <summary>Replaces a document's buffer.</summary>
    /// <inheritdoc cref="NavigateTo(string, int, int, string?)" path="/param|/returns"/>
    Task<bool> SetContent(string fileName, string content, string? project);

    /// <summary>Applies a set of edits to a document's buffer.</summary>
    /// <inheritdoc cref="NavigateTo(string, int, int, string?)" path="/param|/returns"/>
    Task<bool> ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits, string? project);
}
