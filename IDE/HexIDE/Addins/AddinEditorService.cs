using Avalonia.Threading;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// The code windows as an add-in sees them (<see cref="IEditorAccess"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every member that answers true or false is its <c>Try</c> counterpart with the reason dropped</b>, so the
/// two can never disagree about what is refused. The reasons are English by design, as automation's are: an
/// add-in may hand one to a model or a log as readily as show it, and the IDE does not show them itself.
/// </para>
/// <para>
/// A name is found the way automation finds one (<see cref="NamedDocuments"/>): in every loaded project,
/// without regard to case, and refused rather than guessed at when two projects answer to it.
/// </para>
/// </remarks>
public sealed class AddinEditorService(
    IDocumentDockService documentDock,
    IEditorService editorService,
    IProjectManager projectManager) : IEditorAccess
{
    public AddinDocument? GetActiveDocument()
    {
        var editor = documentDock.ActiveDocument as CodeEditorViewModel;
        return editor is null ? null : BuildDocument(editor);
    }

    public AddinSelection? GetSelection()
    {
        var editor = documentDock.ActiveDocument as CodeEditorViewModel;
        if (editor is null) return null;

        var doc     = editor.Document;
        var start   = editor.SelectionStart;
        var length  = editor.SelectionLength;
        var end     = start + length;
        var selected = length > 0 ? doc.GetText(start, length) : string.Empty;

        var startLoc = doc.GetLocation(start);
        var endLoc   = doc.GetLocation(end);
        return new AddinSelection(editor.Identity.Name, selected,
            startLoc.Line, startLoc.Column,
            endLoc.Line, endLoc.Column,
            editor.Identity.Project.Name);
    }

    public void NavigateTo(string fileName, int line, int column) =>
        NavigateTo(fileName, line, column, null);

    /// <inheritdoc/>
    /// <remarks>
    /// Answers as soon as the name is resolved and moves the caret afterwards, as it always has. A position the
    /// document does not have used to throw on the UI thread, where a throw ends the process; it now moves
    /// nothing, and <see cref="TryNavigateTo"/>, which waits, says so.
    /// </remarks>
    public bool NavigateTo(string fileName, int line, int column, string? project)
    {
        var found = Resolve(fileName, project);
        if (found.Document is not { } document)
            return Refused(nameof(NavigateTo), fileName, found.Error);

        Dispatcher.UIThread.Post(() =>
        {
            if (Open(document) is not { } editor) return;
            if (MoveCaret(editor, line, column) is { } refusal)
                Refused(nameof(NavigateTo), document.Display, refusal);
            else if (BringForward(editor) is { } hidden)
                Refused(nameof(NavigateTo), document.Display, hidden);
        });
        return true;
    }

    public async Task<AddinEditResult> TryNavigateTo(string fileName, int line, int column, string? project)
    {
        var found = Resolve(fileName, project);
        if (found.Document is not { } document)
            return Refusal(nameof(TryNavigateTo), fileName, found.Error);

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Open(document) is not { } editor)
                return Refusal(nameof(TryNavigateTo), document.Display, NoWindow(document));
            var refusal = MoveCaret(editor, line, column) ?? BringForward(editor);
            return refusal is null
                ? new AddinEditResult(true)
                : Refusal(nameof(TryNavigateTo), document.Display, refusal);
        });
    }

    public Task SetContent(string fileName, string content) =>
        SetContent(fileName, content, null);

    public async Task<bool> SetContent(string fileName, string content, string? project) =>
        (await TrySetContent(fileName, content, project)).Succeeded;

    public async Task<AddinEditResult> TrySetContent(string fileName, string content, string? project)
    {
        var found = Resolve(fileName, project);
        if (found.Document is not { } document)
            return Refusal(nameof(TrySetContent), fileName, found.Error);

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Open(document) is not { } editor)
                return Refusal(nameof(TrySetContent), document.Display, NoWindow(document));

            // Through the guarded path (#273 tasks 3.9 and 3.19), which automation's set_file_content shares: the
            // whole file with its header unchanged, or the code alone, and the document's own header in front
            // either way. A header that differs is refused, and the add-in is told so.
            var result = editor.ReplaceContent(content);
            return result.Refusal is { } refusal
                ? Refusal(nameof(TrySetContent), document.Display, refusal)
                : new AddinEditResult(true, HeaderKept: result.HeaderKept);
        });
    }

    public Task ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits) =>
        ApplyEdits(fileName, edits, null);

    public async Task<bool> ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits, string? project) =>
        (await TryApplyEdits(fileName, edits, project)).Succeeded;

    public async Task<AddinEditResult> TryApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits, string? project)
    {
        var found = Resolve(fileName, project);
        if (found.Document is not { } document)
            return Refusal(nameof(TryApplyEdits), fileName, found.Error);

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Open(document) is not { } editor)
                return Refusal(nameof(TryApplyEdits), document.Display, NoWindow(document));

            var doc = editor.Document;
            var changes = new List<TextChange>(edits.Count);
            for (var i = 0; i < edits.Count; i++)
            {
                // A position the document does not have used to throw out of GetLineByNumber, which reached
                // the caller as an exception rather than an answer.
                var edit = edits[i];
                var which = Which(i);
                if ((PositionRefusal(doc, document.Display, edit.StartLine, edit.StartColumn, $"{which} starts at")
                     ?? PositionRefusal(doc, document.Display, edit.EndLine, edit.EndColumn, $"{which} ends at")) is { } bad)
                    return Refusal(nameof(TryApplyEdits), document.Display, bad + " Nothing was changed.");

                var startOffset = OffsetOf(doc, edit.StartLine, edit.StartColumn);
                var endOffset = OffsetOf(doc, edit.EndLine, edit.EndColumn);
                if (endOffset < startOffset)
                    return Refusal(nameof(TryApplyEdits), document.Display,
                        $"{which} ends (line {edit.EndLine}, column {edit.EndColumn}) before it starts (line "
                        + $"{edit.StartLine}, column {edit.StartColumn}). Nothing was changed.");
                changes.Add(new TextChange(startOffset, endOffset - startOffset, edit.NewText));
            }

            // All or nothing, through the guarded path (#273 task 3.9): an edit that would change the header or a
            // member's attribute lines refuses the whole set, because applying the rest would leave the add-in's
            // change half made. The refusal names the edit as the add-in sent it, by its place in the set and the
            // positions it gave, rather than where the buffer put it: a column past a line's end means the line's
            // end here, and the add-in would not recognise that column as one it sent.
            return editor.ApplyEdits(changes, i =>
                    $"{Which(i)} (line {edits[i].StartLine}, column {edits[i].StartColumn} to line {edits[i].EndLine}, "
                    + $"column {edits[i].EndColumn})") is { } refusal
                ? Refusal(nameof(TryApplyEdits), document.Display, refusal)
                : new AddinEditResult(true);

            string Which(int i) => edits.Count == 1 ? "The edit" : $"Edit {i + 1} of {edits.Count}";
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The document an add-in named, across every loaded project, or why there is none.
    /// </summary>
    /// <remarks>
    /// It used to be the startup project only, while the editor lookup beside it searched every open tab —
    /// so an add-in could find a document it could not open, or open one it could not find. Both go through
    /// this now. A name matching documents in two loaded projects is refused rather than picked: an add-in that
    /// has been told nothing is better placed than one silently given the wrong file, and one told why can pass
    /// the project.
    /// </remarks>
    private NamedDocument Resolve(string fileName, string? project) =>
        NamedDocuments.Resolve(projectManager.LoadedProjects, fileName, project);

    private static string NoWindow(DocumentIdentity document) =>
        $"No code window could be opened on {document.Display}.";

    /// <summary>Records a refusal, and gives it to the add-in that asked.</summary>
    private static AddinEditResult Refusal(string call, string document, string? reason)
    {
        Refused(call, document, reason);
        return new AddinEditResult(false, reason);
    }

    /// <summary>Records a refusal for a caller that is not told the reason. Always false.</summary>
    private static bool Refused(string call, string document, string? reason)
    {
        Log.Information("Add-in {Call} on {Document} refused: {Reason}", call, document, reason);
        return false;
    }

    /// <summary>Moves the caret, or says why the position is not one the document has.</summary>
    private static string? MoveCaret(CodeEditorViewModel editor, int line, int column)
    {
        if (PositionRefusal(editor.Document, editor.Identity.Display, line, column, "The caret cannot go to") is { } refusal)
            return refusal;
        editor.CaretOffset = OffsetOf(editor.Document, line, column);
        return null;
    }

    /// <summary>
    /// Why a 1-based line and column are not a position in the document, or null when they are. A column past the
    /// end of its line is a position: it means the line's end, as it always has here.
    /// </summary>
    private static string? PositionRefusal(TextDocument doc, string display, int line, int column, string what)
    {
        if (line < 1 || line > doc.LineCount)
            return $"{what} line {line}, which {display} does not have: it has {doc.LineCount} " +
                   $"line{(doc.LineCount == 1 ? "" : "s")}, numbered 1..{doc.LineCount} from the top of the file, " +
                   "header included.";
        if (column < 1)
            return $"{what} column {column} of line {line}: columns are numbered from 1.";
        return null;
    }

    private static int OffsetOf(TextDocument doc, int line, int column)
    {
        var docLine = doc.GetLineByNumber(line);
        return Math.Min(docLine.Offset + column - 1, docLine.EndOffset);
    }

    /// <summary>
    /// Brings an editor's window to the front, or says why it could not.
    /// </summary>
    /// <remarks>
    /// Navigation only. <see cref="Open"/> returns an editor that is already open where it is, so a caret moved in
    /// a background tab moved where the developer could not see it, and the call answered success: AI Chat's
    /// <c>navigate_to</c> told the model it had taken the developer to the line while the screen still showed
    /// another file (hexide-io/HexIDE#273 task 3.19 review). A write does not come forward, so an add-in that
    /// changes a background document does not take the developer's view with it.
    /// </remarks>
    private string? BringForward(CodeEditorViewModel editor)
    {
        if (ReferenceEquals(documentDock.ActiveDocument, editor)) return null;
        return documentDock.TryActivate<CodeEditorViewModel>(e => ReferenceEquals(e, editor))
            ? null
            : $"The caret was moved in {editor.Identity.Display}, but its code window could not be brought to the "
              + "front, so it is not the window the developer sees.";
    }

    /// <summary>
    /// Opens the document's code editor if it is not open, and returns it. One already open is returned where it
    /// is, in front or not.
    /// </summary>
    private CodeEditorViewModel? Open(DocumentIdentity document)
    {
        if (FindEditor(document) is { } already) return already;

        if (document.Module is { } module) editorService.EditCode(module);
        else editorService.EditCode(document.Form);

        return FindEditor(document);
    }

    private CodeEditorViewModel? FindEditor(DocumentIdentity document) =>
        documentDock.OpenDocuments
            .OfType<CodeEditorViewModel>()
            .FirstOrDefault(e => DocumentOf(e) == document);

    /// <summary>The identity of an editor that has one, and null for one not yet initialized.</summary>
    private static DocumentIdentity? DocumentOf(CodeEditorViewModel editor) =>
        editor.ModuleDefinition is { } module ? DocumentIdentity.For(module)
        : editor.FormDefinition is { } form ? DocumentIdentity.For(form)
        : null;

    private static AddinDocumentKind GetKind(CodeEditorViewModel e) =>
        e.ModuleDefinition?.Kind switch
        {
            ModuleKind.UserControl => AddinDocumentKind.UserControl,
            ModuleKind.StandardModule or ModuleKind.ClassModule => AddinDocumentKind.Module,
            // A PropertyPage is neither a form nor a module to an add-in, and there is no kind for it.
            ModuleKind.PropertyPage => AddinDocumentKind.Other,
            _ => AddinDocumentKind.Form,
        };

    /// <summary>
    /// The document as an add-in reads it: the whole file the code window holds, which is the text every position
    /// on this surface counts in (#273 task 3.19). It was the code section until then, while the positions
    /// already counted from the top of the file, so a line found by reading it was out by the header's length.
    /// </summary>
    private static AddinDocument BuildDocument(CodeEditorViewModel editor) =>
        new(editor.Identity.Name,
            editor.Identity.AbsolutePath ?? string.Empty,
            editor.Document.Text,
            GetKind(editor),
            editor.Identity.Project.Name);
}
