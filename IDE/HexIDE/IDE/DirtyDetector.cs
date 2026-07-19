using System;
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

    /// <summary>Dirtiness can't be determined cheaply/safely — skip for now (e.g. a not-open form).</summary>
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
/// editor buffer / designer undo state for open files and the on-disk baseline for not-open files.
/// All view access happens on the UI thread (the watcher dispatches there before calling this).
/// </summary>
public sealed class DirtyDetector(IFileBaselineStore baselineStore)
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

        // A .bas/.cls/.ctl/.pag file's content is exactly module.Code, so the editor buffer / model code
        // can be compared directly against the last-known disk content while open.
        if (t.CodeEditor is not null || t.Designer is not null)
        {
            var codeDirty = t.CodeEditor is not null
                && !string.Equals(t.CodeEditor.Document.Text, module.Code, StringComparison.Ordinal);
            return (codeDirty || designerDirty) ? ReloadDecision.Conflict : ReloadDecision.CleanReload;
        }

        var baseline = baselineStore.TryGet(t.SourcePath);
        if (baseline is null)
            return ReloadDecision.Indeterminate; // unknown saved state — don't risk clobbering edits
        // module.Code is the body only for .bas/.cls; reconstruct the on-disk form (header + body) so the
        // hash matches the baseline (which records the full file written to disk). No-op for .ctl/.pag.
        var modelOnDisk = ModuleFileFormat.HandlesHeader(module.Kind)
            ? ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind)
            : module.Code;
        return string.Equals(FileHasher.Hash(modelOnDisk), baseline.Hash, StringComparison.Ordinal)
            ? ReloadDecision.CleanReload
            : ReloadDecision.Conflict;
    }

    private static ReloadDecision ClassifyForm(WatchedFileTarget t)
    {
        // A .frm is serialized layout + code, so we cannot compare a raw hash to the editor buffer
        // (which holds only the code) or to a re-serialized model (round-trip is not byte-identical).
        // Use the editor buffer vs the model code for code dirtiness and the designer undo stack for
        // layout dirtiness — both only meaningful while the file is open.
        if (!t.IsOpen)
            return ReloadDecision.Indeterminate; // not-open forms handled in a later phase

        var codeDirty = t.CodeEditor is not null
            && !string.Equals(t.CodeEditor.Document.Text, t.Form!.Code, StringComparison.Ordinal);
        var designerDirty = t.Designer is { CanUndo: true };
        return (codeDirty || designerDirty) ? ReloadDecision.Conflict : ReloadDecision.CleanReload;
    }
}
