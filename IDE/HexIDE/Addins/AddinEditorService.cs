using Avalonia.Threading;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;

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
        var fileName = GetFileName(editor);
        return new AddinSelection(fileName, selected,
            startLoc.Line, startLoc.Column,
            endLoc.Line, endLoc.Column);
    }

    public void NavigateTo(string fileName, int line, int column)
    {
        Dispatcher.UIThread.Post(() =>
        {
            EnsureEditorOpen(fileName);
            var editor = FindEditor(fileName);
            if (editor is null) return;
            var docLine = editor.Document.GetLineByNumber(line);
            editor.CaretOffset = Math.Min(docLine.Offset + column - 1, docLine.EndOffset);
        });
    }

    public async Task SetContent(string fileName, string content)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            EnsureEditorOpen(fileName);
            var editor = FindEditor(fileName);
            if (editor is null) return;
            editor.Document.Text = content;
        });
    }

    public async Task ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            EnsureEditorOpen(fileName);
            var editor = FindEditor(fileName);
            if (editor is null) return;

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
        });
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void EnsureEditorOpen(string fileName)
    {
        if (FindEditor(fileName) is not null) return;
        var project = projectManager.StartupProject;
        if (project is null) return;
        var form = project.Forms.FirstOrDefault(f =>
            f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (form is not null) { editorService.EditCode(form); return; }
        var module = project.Modules.FirstOrDefault(m =>
            m.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (module is not null) editorService.EditCode(module);
    }

    private CodeEditorViewModel? FindEditor(string fileName) =>
        documentDock.OpenDocuments
            .OfType<CodeEditorViewModel>()
            .FirstOrDefault(e =>
                GetFileName(e).Equals(fileName, StringComparison.OrdinalIgnoreCase));

    private static string GetFileName(CodeEditorViewModel e) =>
        e.FormDefinition?.Name ?? e.ModuleDefinition?.Name ?? string.Empty;

    private static AddinDocumentKind GetKind(CodeEditorViewModel e) =>
        e.FormDefinition is not null ? AddinDocumentKind.Form : AddinDocumentKind.Module;

    private static AddinDocument BuildDocument(CodeEditorViewModel editor) =>
        new(GetFileName(editor),
            editor.FormDefinition?.AbsolutePath ?? editor.ModuleDefinition?.AbsolutePath ?? string.Empty,
            editor.Document.Text,
            GetKind(editor));
}
