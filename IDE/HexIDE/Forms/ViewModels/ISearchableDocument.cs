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
}
