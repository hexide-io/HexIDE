using System;
using System.Text;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.IDE;

/// <summary>How the file watcher should treat an externally-changed file.</summary>
public enum ReloadDecision
{
    /// <summary>No unsaved edits — safe to silently adopt the disk content.</summary>
    CleanReload,

    /// <summary>The IDE has unsaved edits for this file — a conflict (P1 skips; P2 shows a dialog).</summary>
    Conflict,

    /// <summary>Dirtiness can't be determined cheaply/safely — skip for now (e.g. a not-open module with
    /// no recorded baseline, so there is nothing to compare the model against).</summary>
    Indeterminate,
}

/// <summary>
/// A located externally-changed file: its owning source path, the in-memory model, and any open
/// editor/designer view bound to it. Companion-binary events (<c>.frx</c> etc.) are collapsed onto the
/// owning source path before a target is built.
/// </summary>
public sealed record WatchedFileTarget(
    string SourcePath,
    FormDefinition? Form,
    ModuleDefinition? Module,
    CodeEditorViewModel? CodeEditor,
    FormEditViewModel? Designer)
{
    public bool IsOpen => CodeEditor is not null || Designer is not null;

    /// <summary>
    /// <paramref name="form"/> or <paramref name="module"/> with whichever code window and designer
    /// <paramref name="openDocuments"/> holds for it.
    /// </summary>
    /// <remarks>
    /// A plain form's designer binds the form itself; a UserControl/PropertyPage designer binds the module's
    /// FormPart. Either is matched, so the designer is refreshed in both cases.
    /// </remarks>
    public static WatchedFileTarget For(string sourcePath, FormDefinition? form, ModuleDefinition? module,
        IEnumerable<object> openDocuments)
    {
        var open = openDocuments.ToList();
        var codeEditor = open.OfType<CodeEditorViewModel>().FirstOrDefault(vm =>
            (form is not null && ReferenceEquals(vm.FormDefinition, form)) ||
            (module is not null && ReferenceEquals(vm.ModuleDefinition, module)));
        var designer = open.OfType<FormEditViewModel>().FirstOrDefault(vm =>
            (form is not null && ReferenceEquals(vm.FormDefinition, form)) ||
            (module?.FormPart is not null && ReferenceEquals(vm.FormDefinition, module.FormPart)));
        return new WatchedFileTarget(sourcePath, form, module, codeEditor, designer);
    }
}

/// <summary>
/// Classifies an externally-changed file as a clean reload, a conflict, or indeterminate, using the
/// editor buffer / designer undo state for open files as well as the saved model baseline:
/// a module is compared against the on-disk baseline, and a form — whose serialized layout+code cannot be
/// hashed against a buffer — is compared render-to-render via <see cref="IProjectService"/>.
/// All view access happens on the UI thread (the watcher dispatches there before calling this).
/// </summary>
public sealed class DirtyDetector(IFileBaselineStore baselineStore, IProjectService projectService, IProjectFileFormat format)
{

    public ReloadDecision Classify(WatchedFileTarget t)
    {
        if (t.Module is not null)
            return ClassifyModule(t);
        if (t.Form is not null)
            return ClassifyForm(t);
        return ReloadDecision.Indeterminate;
    }

    private ReloadDecision ClassifyModule(WatchedFileTarget t)
    {
        var module = t.Module!;

        // A UserControl/PropertyPage can be open in the visual designer (binding the module's FormPart);
        // its undo stack signals unsaved layout edits, mirroring the form case.
        var designerDirty = t.Designer is { CanUndo: true };

        // Open views can hold edits not yet copied into the model. Even when they agree, the model
        // must still be checked against its saved baseline below: applying a buffer is not a save (#2862).
        if (t.CodeEditor is not null || t.Designer is not null)
        {
            // BufferBody, not Document.Text: since #273 task 3.2 the buffer is the whole file, so
            // comparing it against Code would report every open document as edited -- and a Conflict is
            // not merely "skip the reload", it queues the ConflictGate and raises a dialog. Every external
            // change would prompt, and the silent CleanReload the file-watcher capability requires would
            // never be reached. The editor owns the split so the two cannot disagree about where it is.
            var codeDirty = t.CodeEditor is not null
                && !string.Equals(t.CodeEditor.BufferBody, module.Code, StringComparison.Ordinal);
            if (codeDirty || designerDirty)
                return ReloadDecision.Conflict;
        }

        // Designer modules hold only the code body; compare complete renders as for a not-open form.
        if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
            return projectService.HasUnsavedChanges(module)
                ? ReloadDecision.Conflict
                : ReloadDecision.CleanReload;

        var baseline = baselineStore.TryGet(t.SourcePath);
        if (baseline is null)
            // Preserve the open-view verdict when no model baseline is available.
            return t.IsOpen ? ReloadDecision.CleanReload : ReloadDecision.Indeterminate;
        // Rebuild header + body as a save writes it, in the file's retained encoding (including its BOM).
        var modelOnDisk = format.RenderModule(module, System.IO.Path.GetFileName(t.SourcePath)).Text;
        try
        {
            return string.Equals(FileHasher.Hash(Vb6TextFile.Encode(modelOnDisk, module.TextEncoding)),
                baseline.Hash, StringComparison.Ordinal)
                ? ReloadDecision.CleanReload
                : ReloadDecision.Conflict;
        }
        catch (EncoderFallbackException)
        {
            return ReloadDecision.Conflict; // an edit the source encoding cannot hold is still an unsaved edit
        }
    }

    private ReloadDecision ClassifyForm(WatchedFileTarget t)
    {
        // A .frm is serialized layout + code, so we cannot compare a raw hash to a re-serialized model
        // (round-trip is not byte-identical), nor to the editor buffer: that holds the whole file since #273,
        // but its header is the designer text as last read, saved or committed, which says nothing about layout
        // still in the designer. Use the buffer's code section vs the model code for code dirtiness and the
        // designer undo stack for layout dirtiness — both only meaningful while the file is open.
        //
        // Also ask the save prompt's render-vs-render question, even with open views: a buffer
        // applied to the model can agree with it while both still contain unsaved code (#2862).
        // BufferBody rather than the raw buffer -- see the note in ClassifyModule.
        var codeDirty = t.CodeEditor is not null
            && !string.Equals(t.CodeEditor.BufferBody, t.Form!.Code, StringComparison.Ordinal);
        var designerDirty = t.Designer is { CanUndo: true };
        return (codeDirty || designerDirty || projectService.HasUnsavedChanges(t.Form!))
            ? ReloadDecision.Conflict
            : ReloadDecision.CleanReload;
    }
}
