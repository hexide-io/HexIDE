using System;
using System.Collections.Generic;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

// Measured in docs/vb6-fidelity-oracle/view-last-position.md and issue #69 (oracle PR #5426).
internal sealed class LastPositionHistory
{
    private readonly List<LastPositionLocation> entries = new();
    private readonly HashSet<ProjectDefinition> projects = new();
    private int next;
    internal IReadOnlyList<LastPositionLocation> Entries => entries;
    internal event Action? Changed;

    internal LastPositionHistory(IEventBus events)
    {
        events.Subscribe<LastPositionRecordedEvent>(e => Record(e.Document, e.Line));
        events.Subscribe<ProjectUnloadedEvent>(e =>
        {
            RemoveWhere(entry => entry.Document.IsIn(e.Project));
            if (!projects.Remove(e.Project)) return;
            e.Project.FormDeleted -= FormDeleted;
            e.Project.ModuleDeleted -= ModuleDeleted;
        });
    }

    private void FormDeleted(ProjectDefinition project, FormDefinition form) =>
        RemoveWhere(entry => entry.Document == DocumentIdentity.For(form));
    private void ModuleDeleted(ProjectDefinition project, ModuleDefinition module) =>
        RemoveWhere(entry => entry.Document == DocumentIdentity.For(module));

    private void RemoveWhere(Predicate<LastPositionLocation> predicate)
    {
        if (entries.RemoveAll(predicate) == 0) return;
        next = 0;
        Changed?.Invoke();
    }

    internal void Record(DocumentIdentity document, int line)
    {
        if (document.Module is { } member && !document.Project.Modules.Contains(member)) return;
        if (document.Form is { } form && !document.Project.Forms.Contains(form)) return;
        if (projects.Add(document.Project))
        {
            document.Project.FormDeleted += FormDeleted;
            document.Project.ModuleDeleted += ModuleDeleted;
        }
        var entry = new LastPositionLocation(document, line);
        if (entries.Count > 0 && entries[0] == entry) return;
        entries.Insert(0, entry);
        if (entries.Count > 8) entries.RemoveAt(8);
        next = 0;
        Changed?.Invoke();
    }

    internal void GoBack(IEditorService editors, CodeEditorViewModel? active)
    {
        if (entries.Count == 0) return;
        var entry = entries[next];
        next = (next + 1) % entries.Count;
        // Choose before leaving an edited line: that new entry belongs to the following press.
        active?.LeaveLastPositionLine();
        if (entry.Document.Module is { } module) editors.EditCode(module);
        else editors.EditCode(entry.Document.Form);
        if (editors.FindOpenCode(entry.Document) is not { } target) return;
        var line = target.Document.GetLineByNumber(Math.Clamp(entry.Line, 1, target.Document.LineCount));
        target.NavigateToOffset(line.Offset);
    }
}

internal sealed record LastPositionLocation(DocumentIdentity Document, int Line);
internal sealed record LastPositionRecordedEvent(DocumentIdentity Document, int Line) : IEvent;
