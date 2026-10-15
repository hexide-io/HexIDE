using System;
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

        var baseline = baselineStore.TryGet(t.SourcePath);
        if (baseline is null)
            return ReloadDecision.Indeterminate; // unknown saved state — don't risk clobbering edits
        // module.Code is the code section only, so a .bas/.cls is rebuilt as a save writes it (header + body)
        // to be hashed against the baseline, which records the bytes on disk. Two things stop the two matching,
        // both hexide-io/HexIDE#730, and each classes a module with no unsaved edits Conflict on an external
        // change: the model is hashed as UTF-8 text where the baseline is the file's own bytes, so a Latin-1
        // accent differs; and a .ctl/.pag is hashed as module.Code alone, which is its file only when its
        // designer block did not parse, and otherwise the code after the root End. This used to call the
        // .ctl/.pag case a no-op.
        var modelOnDisk = ModuleFileFormat.HandlesHeader(module.Kind)
            ? ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader)
            : module.Code;
        return string.Equals(FileHasher.Hash(modelOnDisk), baseline.Hash, StringComparison.Ordinal)
            ? ReloadDecision.CleanReload
            : ReloadDecision.Conflict;
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
