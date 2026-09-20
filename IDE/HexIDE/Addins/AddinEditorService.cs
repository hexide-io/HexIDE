using Avalonia.Threading;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Addins;

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

    public bool NavigateTo(string fileName, int line, int column, string? project)
    {
        if (Resolve(fileName, project) is not { } document) return false;

        Dispatcher.UIThread.Post(() =>
        {
            if (Open(document) is not { } editor) return;
            var docLine = editor.Document.GetLineByNumber(line);
            editor.CaretOffset = Math.Min(docLine.Offset + column - 1, docLine.EndOffset);
        });
        return true;
    }

    public Task SetContent(string fileName, string content) =>
        SetContent(fileName, content, null);

    public async Task<bool> SetContent(string fileName, string content, string? project)
    {
        if (Resolve(fileName, project) is not { } document) return false;

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Open(document) is not { } editor) return false;
            editor.Document.Text = content;
            return true;
        });
    }

    public Task ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits) =>
        ApplyEdits(fileName, edits, null);

    public async Task<bool> ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits, string? project)
    {
        if (Resolve(fileName, project) is not { } document) return false;

        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Open(document) is not { } editor) return false;

            var doc = editor.Document;
            // Apply in reverse order so earlier offsets stay valid
            foreach (var edit in edits.OrderByDescending(e => (e.StartLine, e.StartColumn)))
            {
                var startLine = doc.GetLineByNumber(edit.StartLine);
                var startOffset = Math.Min(startLine.Offset + edit.StartColumn - 1, startLine.EndOffset);
                var endLine = doc.GetLineByNumber(edit.EndLine);
                var endOffset = Math.Min(endLine.Offset + edit.EndColumn - 1, endLine.EndOffset);
                doc.Replace(startOffset, endOffset - startOffset, edit.NewText);
            }
            return true;
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The document an add-in named, across every loaded project.
    /// </summary>
    /// <remarks>
    /// It used to be the startup project only, while the editor lookup beside it searched every open tab —
    /// so an add-in could find a document it could not open, or open one it could not find. Both go through
    /// this now. A name matching documents in two loaded projects answers null rather than picking: an
    /// add-in that has been told nothing is better placed than one silently given the wrong file.
    /// </remarks>
    private DocumentIdentity? Resolve(string fileName, string? project)
    {
        var found = DocumentLookup.Find(projectManager.LoadedProjects, fileName, project);
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Opens the document's code editor if it is not open, and returns it.</summary>
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

    private static AddinDocument BuildDocument(CodeEditorViewModel editor) =>
        new(editor.Identity.Name,
            editor.Identity.AbsolutePath ?? string.Empty,
            editor.Document.Text,
            GetKind(editor),
            editor.Identity.Project.Name);
}
