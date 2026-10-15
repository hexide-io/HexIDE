using AvaloniaEdit.Document;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// A document Find can search: a text buffer, a caret, and a selection.
/// </summary>
/// <remarks>
/// <para>
/// Find used to ask the dock for <see cref="CodeEditorViewModel"/> by name, which made "can this be
/// searched?" mean "is this a VB6 code window?". Those are different questions, and the carried-file
/// editor is the case that proves it: <see cref="RelatedDocumentEditorViewModel"/> owns a real
/// <see cref="TextDocument"/>, is a real active document, and was refused anyway — with no message, so a
/// user could not tell the refusal from a term that genuinely was not there (hexide-io/HexIDE#363).
/// </para>
/// <para>
/// Deliberately the smallest surface the search methods actually use: they take a
/// <see cref="TextDocument"/> and report an offset, and the caller needs somewhere to put the caret and
/// the selection. Nothing about VB6, modules, forms or language servers belongs here — which is what
/// lets a plain text editor satisfy it without pretending to be a code window.
/// </para>
/// </remarks>
public interface ISearchableDocument
{
    /// <summary>The live buffer. Live, not a snapshot: a search must see unsaved edits.</summary>
    TextDocument Document { get; }

    int CaretOffset { get; set; }

    int SelectionStart { get; set; }

    int SelectionLength { get; set; }

    /// <summary>
    /// True when replacing <paramref name="length"/> characters at <paramref name="offset"/> would change a
    /// region only the IDE may write: a code window's header, or a member's attribute lines, including by
    /// removing the line break those lines hang from (hexide-io/HexIDE#273 phase 3). False for a document
    /// that has no such regions, which is every one except a code window.
    /// </summary>
    bool IsReadOnlyRegion(int offset, int length) => false;

    /// <summary>
    /// <see cref="IsReadOnlyRegion"/> against the buffer as it stands now, found once, for a caller about to
    /// make many replacements.
    /// </summary>
    /// <remarks>
    /// Finding the regions reads the whole buffer, so asking afresh at every match of a Replace All is
    /// quadratic in a large module. The snapshot stays true for a caller that works from the end of the
    /// buffer backwards, because a replacement never moves anything before it.
    /// </remarks>
    Func<int, int, bool> SnapshotReadOnlyRegions() => static (_, _) => false;
}
