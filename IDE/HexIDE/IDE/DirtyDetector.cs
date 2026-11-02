using System;
using System.Text;
using HexIDE.Forms.ViewModels;
using HexIDE.Projects;
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
/// editor buffer / designer undo state for open files. For not-open files there are no views to consult:
/// a module is compared against the on-disk baseline, and a form — whose serialized layout+code cannot be
/// hashed against a buffer — is compared render-to-render via <see cref="IProjectService"/>.
/// All view access happens on the UI thread (the watcher dispatches there before calling this).
/// </summary>
public sealed class DirtyDetector(IFileBaselineStore baselineStore, IProjectService projectService)
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

        // While a window is open, the question is whether it holds anything the model does not: the code
        // window's code section against module.Code, and the designer's undo stack for layout. The disk is not
        // consulted. (This used to say a file's content was exactly module.Code, which is true of no kind that
        // splits a header off at load: module.Code is the code section, the header held apart.)
        if (t.CodeEditor is not null || t.Designer is not null)
        {
            // BufferBody, not Document.Text: since #273 task 3.2 the buffer is the whole file, so
            // comparing it against Code would report every open document as edited -- and a Conflict is
            // not merely "skip the reload", it queues the ConflictGate and raises a dialog. Every external
            // change would prompt, and the silent CleanReload the file-watcher capability requires would
            // never be reached. The editor owns the split so the two cannot disagree about where it is.
            var codeDirty = t.CodeEditor is not null
                && !string.Equals(t.CodeEditor.BufferBody, module.Code, StringComparison.Ordinal);
            return (codeDirty || designerDirty) ? ReloadDecision.Conflict : ReloadDecision.CleanReload;
        }

        // Designer modules hold only the code body; compare complete renders as for a not-open form.
        if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
            return projectService.HasUnsavedChanges(module)
                ? ReloadDecision.Conflict
                : ReloadDecision.CleanReload;

        var baseline = baselineStore.TryGet(t.SourcePath);
        if (baseline is null)
            return ReloadDecision.Indeterminate; // unknown saved state — don't risk clobbering edits
        // Rebuild header + body as a save writes it, in the file's retained encoding (including its BOM).
        var modelOnDisk = ModuleFileFormat.HandlesHeader(module.Kind)
            ? ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader)
            : module.Code;
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
        // With no views to consult, ask the project service the render-vs-render question the save
        // prompt uses: has the model changed since it was last loaded, saved, or reloaded? That sidesteps
        // the round-trip-fidelity problem, because both sides of the comparison are our own render.
        // Skipping here instead would leave the cached model stale — and since SaveProject writes every
        // form unconditionally, the next save would put the stale model back over the external change.
        if (!t.IsOpen)
            return projectService.HasUnsavedChanges(t.Form!)
                ? ReloadDecision.Conflict
                : ReloadDecision.CleanReload;

        // BufferBody rather than the raw buffer -- see the note in ClassifyModule.
        var codeDirty = t.CodeEditor is not null
            && !string.Equals(t.CodeEditor.BufferBody, t.Form!.Code, StringComparison.Ordinal);
        var designerDirty = t.Designer is { CanUndo: true };
        return (codeDirty || designerDirty) ? ReloadDecision.Conflict : ReloadDecision.CleanReload;
    }
}
