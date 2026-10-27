using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Dock.Model.Core;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using HexIDE.TwinBasic;

namespace HexIDE.IDE;

public enum ClosedDocumentKind { Code, Designer, Tool }

public sealed record ClosedDocument(string Title, object Identity, ProjectDefinition? Project,
    Action<IEditorService, Action<IDockable>> Open, ClosedDocumentKind Kind = ClosedDocumentKind.Tool)
{
    internal ArchiveDocumentViewModel? Archive { get; init; }
}

public sealed class RecentlyClosedDocuments : IDisposable
{
    private readonly ObservableCollection<ClosedDocument> entries = new();
    private readonly HashSet<ProjectDefinition> projects = new();
    private readonly ConditionalWeakTable<ProjectDefinition, object> unloaded = new();
    public ReadOnlyObservableCollection<ClosedDocument> Entries { get; }

    public RecentlyClosedDocuments() => Entries = new(entries);

    internal void Record(IDockable tab)
    {
        ClosedDocument? entry = tab switch
        {
            CodeEditorViewModel code when code.OpenDocument is { } id => new(tab.Title, id, id.Project,
                (editors, _) =>
                {
                    if (id.Module is { } module) editors.EditCode(module);
                    else editors.EditCode(id.Form);
                }, ClosedDocumentKind.Code),
            FormEditViewModel designer when designer.FormDefinition is { } form => new(tab.Title,
                DocumentIdentity.For(form), form.Owner, (editors, _) => editors.EditForm(form), ClosedDocumentKind.Designer),
            RelatedDocumentEditorViewModel related when related.RelatedDocument is { } doc => new(tab.Title,
                doc, doc.Owner, (editors, _) =>
                {
                    editors.EditRelatedDocument(doc);
                    editors.RestoreCaret(doc, related.CaretOffset);
                }, ClosedDocumentKind.Code),
            ProjectDocumentViewModel project when project.Project is { } definition => new(tab.Title,
                definition, definition, (editors, _) => editors.EditProject(definition), ClosedDocumentKind.Code),
            ArchiveDocumentViewModel archive => new(tab.Title, archive.Entry.Identity, null,
                (_, open) => open(archive.Reopen()), ClosedDocumentKind.Code) { Archive = archive },
            BaseEditorWindowViewModel => null,
            _ => new(tab.Title, tab, null, (_, openTool) => openTool(tab))
        };
        if (entry is null || entry.Project is { } owner && (unloaded.TryGetValue(owner, out _) || !IsMember(entry))) return;
        if (tab is CodeEditorViewModel editor)
        {
            var offset = editor.CaretOffset;
            var open = entry.Open;
            var identity = (DocumentIdentity)entry.Identity;
            entry = entry with { Open = (editors, tool) =>
            {
                open(editors, tool);
                editors.RestoreCaret(identity, offset);
            } };
        }
        if (entry.Project is { } projectOwner && projects.Add(projectOwner))
        {
            projectOwner.FormDeleted += OnFormDeleted;
            projectOwner.ModuleDeleted += OnModuleDeleted;
            projectOwner.RelatedDocumentDeleted += OnRelatedDeleted;
        }
        entries.Insert(0, entry);
        if (entries.Count > 20) entries.RemoveAt(20);
    }

    public void Reopen(IEditorService editors, Action<IDockable> openTool, ClosedDocument? entry = null)
    {
        entry ??= entries.FirstOrDefault();
        if (entry is null || !entries.Remove(entry)) return;
        entry.Open(editors, openTool);
    }

    internal void CloseArchive(string? path)
    {
        foreach (var entry in entries.Where(e => e.Archive is { } archive &&
                     (path is null || ArchiveEntryIdentity.ContainerPathComparer.Equals(archive.Entry.Identity.ContainerPath, path))).ToArray())
            entries.Remove(entry);
    }

    internal void RefreshArchive(ArchiveProjectSnapshot snapshot)
    {
        foreach (var entry in entries.Where(e => e.Archive is { } archive &&
                     ArchiveEntryIdentity.ContainerPathComparer.Equals(archive.Entry.Identity.ContainerPath, snapshot.ContainerPath)).ToArray())
        {
            var archive = entry.Archive!;
            if (!snapshot.TryGetEntry(archive.Entry.Identity, out var replacement)) entries.Remove(entry);
            else
            {
                var offset = archive.CaretOffset;
                archive.Update(replacement);
                archive.CaretOffset = Math.Clamp(offset, 0, archive.Document.TextLength);
            }
        }
    }

    private static bool IsMember(ClosedDocument entry) => entry.Identity switch
    {
        DocumentIdentity id => id.Module is { } module ? id.Project.Modules.Contains(module) : id.Project.Forms.Contains(id.Form!),
        RelatedDocumentDefinition doc => doc.Owner.RelatedDocuments.Contains(doc),
        _ => true
    };

    private void Prune() { foreach (var entry in entries.Where(e => !IsMember(e)).ToArray()) entries.Remove(entry); }
    private void OnFormDeleted(ProjectDefinition project, FormDefinition form) => Prune();
    private void OnModuleDeleted(ProjectDefinition project, ModuleDefinition module) => Prune();
    private void OnRelatedDeleted(ProjectDefinition project, RelatedDocumentDefinition document) => Prune();

    public void Unload(ProjectDefinition project)
    {
        unloaded.GetValue(project, static _ => new object());
        foreach (var entry in entries.Where(e => e.Project == project).ToArray()) entries.Remove(entry);
        Unsubscribe(project);
    }

    private void Unsubscribe(ProjectDefinition project)
    {
        project.FormDeleted -= OnFormDeleted;
        project.ModuleDeleted -= OnModuleDeleted;
        project.RelatedDocumentDeleted -= OnRelatedDeleted;
        projects.Remove(project);
    }

    public void Dispose()
    {
        foreach (var project in projects.ToArray()) Unsubscribe(project);
        entries.Clear();
        unloaded.Clear();
    }
}
