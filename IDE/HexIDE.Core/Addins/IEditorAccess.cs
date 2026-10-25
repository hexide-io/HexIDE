namespace HexIDE.Addins;

/// <summary>
/// The code windows, as an add-in reads and changes them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A document's content is its whole file, and every position counts from the top of it.</b> A form's
/// <c>VERSION</c> line and designer block, a class's header, and the <c>Attribute</c> lines that open the code
/// come first, as the code window shows them, so line 1 is the file's first line here, in the code window's
/// margin, in a diagnostic and in the debugger alike. Lines and columns are 1-based.
/// </para>
/// <para>
/// <b>The header is the IDE's to change.</b> It follows the form designer, the Properties window and a rename,
/// and nothing an add-in sends changes it. A replacement may be the whole file with its header exactly as the
/// document has it, or the code alone, in which case the document's own header is kept in front of it; a form's
/// code may also be sent with the <c>Attribute</c> lines it opens with, as earlier versions of this surface
/// handed it out. A replacement whose header differs is refused. An edit to a range is refused if it would change
/// the header or a member's <c>Attribute</c> lines, and the refusal names the member.
/// </para>
/// <para>
/// <b>A member's <c>Attribute</c> lines are the IDE's too</b>: the run of them directly under a procedure's,
/// property's or module-level variable's declaration, which give it its description, help topic or dispatch id. A
/// replacement may move them with their member, as it moves the procedure, and rename them with it, and they may go
/// with the member's declaration; otherwise it sends them exactly as the document has them. One that changes,
/// adds or drops a member's <c>Attribute</c> lines is refused, and the refusal names each member
/// (hexide-io/HexIDE#273 task 4.5b). Renaming a member and changing its lines, or retyping its declaration, in one
/// replacement is refused as lines added to the new name. <c>Attribute</c> lines that describe nothing, under a
/// blank line or a comment say, are ordinary text.
/// </para>
/// <para>
/// <b>The members that start <c>Try</c> say why a call was refused</b>, and are the ones to call. The others
/// answer only true or false, or nothing, and are kept so that an add-in built against an earlier version keeps
/// running unchanged: they do exactly what their <c>Try</c> counterparts do and drop the reason.
/// </para>
/// </remarks>
public interface IEditorAccess
{
    /// <summary>The document in the active code window, or null when the active window is not a code window.</summary>
    AddinDocument? GetActiveDocument();

    AddinSelection? GetSelection();

    /// <inheritdoc cref="NavigateTo(string, int, int, string?)"/>
    /// <remarks>
    /// Kept for add-ins written before a document could be named by its project. It searches every loaded
    /// project and does nothing at all when the name is unknown or names more than one document;
    /// <see cref="TryNavigateTo"/> says which of those happened.
    /// </remarks>
    void NavigateTo(string fileName, int line, int column);

    /// <inheritdoc cref="TrySetContent(string, string, string?)"/>
    /// <remarks>Kept for add-ins written before a document could be named by its project.</remarks>
    Task SetContent(string fileName, string content);

    /// <inheritdoc cref="TryApplyEdits(string, IReadOnlyList{AddinTextEdit}, string?)"/>
    /// <remarks>Kept for add-ins written before a document could be named by its project.</remarks>
    Task ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits);

    /// <summary>
    /// Puts the caret on a document and brings its code window to the front, opening it if it is not already
    /// open. The caret moves shortly after this returns, on the UI thread; on HexIDE's own host
    /// <see cref="TryNavigateTo"/> waits for it.
    /// </summary>
    /// <param name="fileName">The document's VB6 name, in any case.</param>
    /// <param name="line">
    /// 1-based, counted from the top of the file with its header included, as the code window numbers it.
    /// </param>
    /// <param name="column">1-based.</param>
    /// <param name="project">
    /// The project to look in, by name, or null to search every loaded one. A bare name that answers to
    /// documents in two loaded projects is refused rather than guessed at.
    /// </param>
    /// <returns>
    /// False when no loaded project has the name given as <paramref name="project"/>, when no such document was
    /// found, or when the name was ambiguous. A line or column the document does not have moves nothing.
    /// </returns>
    bool NavigateTo(string fileName, int line, int column, string? project);

    /// <summary>Replaces a document's content, keeping its header.</summary>
    /// <remarks>
    /// Send the whole file with its header exactly as the document has it, which is what
    /// <see cref="AddinDocument.Content"/> holds, or the code alone. Either way the document keeps its own
    /// header. A member's <c>Attribute</c> lines go with it: moved or renamed with the member, or deleted with its
    /// declaration, and otherwise exactly as the document has them.
    /// </remarks>
    /// <inheritdoc cref="NavigateTo(string, int, int, string?)" path="/param"/>
    /// <returns>
    /// False when no loaded project has the name given as <paramref name="project"/>, when no such document was
    /// found, when the name was ambiguous, or when the content would have changed the document's header or a
    /// member's <c>Attribute</c> lines, in which case nothing was written. <see cref="TrySetContent"/> says which.
    /// </returns>
    Task<bool> SetContent(string fileName, string content, string? project);

    /// <summary>Applies a set of edits to a document.</summary>
    /// <remarks>
    /// All or nothing: if any edit would change the document's header or a member's <c>Attribute</c> lines, or
    /// names a position the document does not have, none is applied. The resulting text is checked too: adding
    /// a described run (including a whole new described member), promoting an orphan or extending a run is
    /// refused, with a reason naming the members. Deleting a run with its declaration remains refused.
    /// </remarks>
    /// <inheritdoc cref="NavigateTo(string, int, int, string?)" path="/param"/>
    /// <returns>
    /// False when no loaded project has the name given as <paramref name="project"/>, when no such document was
    /// found, when the name was ambiguous, or when an edit was refused, in which case nothing was written.
    /// <see cref="TryApplyEdits"/> says which.
    /// </returns>
    Task<bool> ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits, string? project);

    /// <summary>
    /// Puts the caret on a document and brings its code window to the front, opening it if it is not already
    /// open, and says why when it does not.
    /// </summary>
    /// <inheritdoc cref="NavigateTo(string, int, int, string?)" path="/param"/>
    /// <returns>
    /// Whether the caret was moved and its window brought to the front, and if not, why: no loaded project of
    /// the name given as <paramref name="project"/>, no such document, a name two loaded projects share, a line
    /// or column the document does not have, or a window that could not be brought to the front.
    /// </returns>
    /// <remarks>
    /// <b>HexIDE's own host completes once the caret is there</b>, so a <see cref="GetSelection"/> straight after
    /// reads the new position. The body here does not: it serves only a host that implements nothing newer than
    /// <see cref="NavigateTo(string, int, int, string?)"/>, which answers as soon as the name resolves and moves
    /// the caret afterwards, and which can say that a call failed but not why.
    /// </remarks>
    Task<AddinEditResult> TryNavigateTo(string fileName, int line, int column, string? project) =>
        Task.FromResult(NavigateTo(fileName, line, column, project)
            ? new AddinEditResult(true)
            : AddinEditResult.WithoutReason);

    /// <summary>Replaces a document's content, keeping its header, and says why when it does not.</summary>
    /// <inheritdoc cref="SetContent(string, string, string?)" path="/remarks"/>
    /// <inheritdoc cref="NavigateTo(string, int, int, string?)" path="/param"/>
    /// <returns>
    /// Whether the content was written, and if not, why: no loaded project of the name given as
    /// <paramref name="project"/>, no such document, a name two loaded projects share, a header that differs
    /// from the document's, or a change to a member's <c>Attribute</c> lines, which names each member it reaches.
    /// Nothing is written when it is refused. <see cref="AddinEditResult.HeaderKept"/> says whether the document's
    /// header was put in front of what was sent.
    /// </returns>
    async Task<AddinEditResult> TrySetContent(string fileName, string content, string? project) =>
        await SetContent(fileName, content, project).ConfigureAwait(false)
            ? new AddinEditResult(true)
            : AddinEditResult.WithoutReason;

    /// <summary>Applies a set of edits to a document, and says why when it does not.</summary>
    /// <inheritdoc cref="ApplyEdits(string, IReadOnlyList{AddinTextEdit}, string?)" path="/remarks"/>
    /// <inheritdoc cref="NavigateTo(string, int, int, string?)" path="/param"/>
    /// <returns>
    /// Whether the edits were applied, and if not, why: no loaded project of the name given as
    /// <paramref name="project"/>, no such document, a name two loaded projects share, an edit that would change
    /// a read-only part of the document, or a position the document does not have. A refusal names the edit by
    /// its place in the set and the positions it was sent with.
    /// Nothing is applied when any edit is refused.
    /// </returns>
    async Task<AddinEditResult> TryApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits, string? project) =>
        await ApplyEdits(fileName, edits, project).ConfigureAwait(false)
            ? new AddinEditResult(true)
            : AddinEditResult.WithoutReason;
}
