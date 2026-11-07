using System;
using System.IO;
using System.Linq;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using Serilog;

namespace HexIDE.IDE;

public partial class EditorService
{
    internal DocumentSessionStore DocumentSessions { private get; set; } = new();
    private static readonly StringComparer SessionPathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public void SaveOpenDocuments(ProjectDefinition project)
    {
        var documents = documentDockService.AllTabs.OfType<BaseEditorWindowViewModel>()
            .Select(tab => StateFor(tab, project)).OfType<OpenDocumentState>().ToArray();
        DocumentSessions.Save(project.AbsolutePath, documents);
    }

    private OpenDocumentState? StateFor(BaseEditorWindowViewModel tab, ProjectDefinition project)
    {
        (string? kind, string? path) = tab switch
        {
            CodeEditorViewModel { OpenDocument: { } document } when document.Project == project =>
                ("code", document.AbsolutePath),
            FormEditViewModel { FormDefinition: { } form } when form.Owner == project =>
                ("designer", form.AbsolutePath),
            RelatedDocumentEditorViewModel { RelatedDocument: { } related } when related.Owner == project =>
                ("related", related.AbsolutePath),
            ProjectDocumentViewModel view when view.Project == project => ("project", project.AbsolutePath),
            _ => (null, null)
        };
        return kind is null || string.IsNullOrEmpty(path) ? null
            : new(kind, path, ReferenceEquals(tab, documentDockService.ActiveTab));
    }

    public void RestoreOpenDocuments(ProjectDefinition project)
    {
        OpenDocumentState? active = null;
        foreach (var entry in DocumentSessions.Load(project.AbsolutePath))
        {
            if (entry is null || string.IsNullOrEmpty(entry.Path) || !File.Exists(entry.Path)) continue;
            try
            {
                var form = project.Forms.FirstOrDefault(f => SessionPathComparer.Equals(f.AbsolutePath, entry.Path));
                var module = project.Modules.FirstOrDefault(m => SessionPathComparer.Equals(m.AbsolutePath, entry.Path));
                switch (entry.Kind)
                {
                    case "code":
                        if (module is not null) EditCode(module);
                        else if (form is not null) EditCode(form);
                        break;
                    case "designer":
                        EditForm(form ?? module?.FormPart);
                        break;
                    case "related":
                        EditRelatedDocument(project.RelatedDocuments.FirstOrDefault(d => SessionPathComparer.Equals(d.AbsolutePath, entry.Path)));
                        break;
                    case "project" when SessionPathComparer.Equals(project.AbsolutePath, entry.Path):
                        EditProject(project);
                        break;
                }
                if (entry.Active) active = entry;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not reopen {Document}", entry.Path);
            }
        }
        if (active is not null)
            documentDockService.TryActivateAny(tab => tab is BaseEditorWindowViewModel editor
                && StateFor(editor, project) is { } state && state.Kind == active.Kind
                && SessionPathComparer.Equals(state.Path, active.Path));
    }
}
