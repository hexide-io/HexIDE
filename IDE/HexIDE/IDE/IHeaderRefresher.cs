using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// Keeps the header the code window shows in front of a document's code in step with the document, and
/// moves that document's marks when the header's line count changes.
/// </summary>
/// <remarks>
/// <para>
/// Since task 3.2 of hexide-io/HexIDE#273 the code window holds the whole file, not the code section: one
/// line number now means the same thing to the editor, a language server, the interpreter and the debugger.
/// The invariant that buys is <b>the buffer is the text of the file as it stands, and after a save it is the
/// text that save wrote</b> — which only holds if something moves the header when the thing it describes
/// moves. That is this.
/// </para>
/// <para>
/// <b>Three triggers, one write path.</b> A committed designer change (<c>FormLayoutChangedEvent</c>), a
/// save (the header that was just written, before the save is announced, through <see cref="Saved"/>), and a
/// reload. All three land in <see cref="ApplyHeader"/> or in <see cref="ShiftMarks"/>, so a document's marks
/// cannot move for one trigger and not for another.
/// </para>
/// <para>
/// <b>It works with no window open, and that is the point of the seam rather than an accident.</b>
/// Breakpoints and bookmarks are bare integers held per document, not anchors in a buffer, so a header that
/// grows by a line moves every mark below it whether or not anybody is looking at the code. A refresher
/// that lived on the code editor would move the marks of the documents that happen to be open and silently
/// leave the rest pointing at the wrong statements.
/// </para>
/// </remarks>
public interface IHeaderRefresher
{
    /// <summary>
    /// Trigger one: a committed change to a form's designer half. Re-renders the designer text and makes it
    /// the header, when the render moved and when the form is one HexIDE may render at all.
    /// </summary>
    void LayoutChanged(FormDefinition form);

    /// <summary>
    /// Makes <paramref name="header"/> the document's header: recorded on the model, the marks below it
    /// moved, and an open buffer re-headed in place. Does nothing when it is the header already.
    /// </summary>
    void ApplyHeader(FormDefinition form, string header);

    /// <summary>
    /// Trigger two, a save: makes an open buffer the text the save just wrote, as one undo entry the code
    /// window's Undo never stops at. That is the <paramref name="header"/> it rendered, for a kind with a
    /// designer half (through <see cref="ApplyHeader"/>), and the qualifiers it rewrote in members' attribute
    /// runs (#273 task 4.4).
    /// </summary>
    /// <param name="document">The document written.</param>
    /// <param name="header">
    /// The designer half the save rendered; null for a <c>.bas</c> or <c>.cls</c>, whose header the save does not
    /// re-render.
    /// </param>
    void Saved(DocumentIdentity document, string? header);

    /// <summary>
    /// Makes the <c>Attribute VB_Name</c> the document carries say the document's current name — on the
    /// model, and in an open buffer — when it carries one and it says something else.
    /// </summary>
    /// <remarks>
    /// <see cref="LayoutChanged"/> calls this, because a form's rename is a change to its root control and
    /// reaches the IDE as a designer commit. A module has no rename gesture at all yet
    /// (hexide-io/HexIDE#493); when it gets one, this is what it calls.
    /// </remarks>
    void NameChanged(DocumentIdentity document);

    /// <summary>
    /// Moves a document's breakpoints and bookmarks by the change in the header's line count. Marks inside
    /// the old header stay where they are.
    /// </summary>
    void ShiftMarks(DocumentIdentity document, string oldHeader, string newHeader);
}
