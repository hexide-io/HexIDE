using System;
using System.IO;
using System.Linq;
using System.Text;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.IDE;

/// <summary>Uses live buffers first; searching never flushes an editor or opens a non-matching file.</summary>
internal sealed class FindProjectDocuments(IDocumentDockService dock, IProjectManager projects,
    IEditorService editors, Func<ProjectDefinition?> selectedProject)
{
    public ProjectDefinition? CurrentProject => dock.ActiveDocument switch
    {
        CodeEditorViewModel code => code.ModuleDefinition?.Owner ?? code.FormDefinition?.Owner,
        FormEditViewModel designer => designer.FormDefinition?.Owner,
        RelatedDocumentEditorViewModel related => related.RelatedDocument?.Owner,
        ProjectDocumentViewModel project => project.Project,
        _ => null
    } ?? selectedProject() ?? projects.StartupProject;

    public bool HasGroup => projects.CurrentGroup is not null;

    public ISearchableDocument? Lookup(object definition) => dock.FindSearchableDocument(definition);

    public List<ISearchableDocument> GetDocuments(bool allProjects)
    {
        var result = new List<ISearchableDocument>();
        foreach (var project in projects.LoadedProjects)
        {
            if (!allProjects && project != CurrentProject) continue;
            foreach (var module in project.Modules)
                result.Add(Lookup(module) ?? new UnopenedDocument(FormCodeText.WholeFile(module),
                    FormCodeText.Prefix(module).Length, () => { editors.EditCode(module); return Lookup(module); }));
            foreach (var form in project.Forms)
            {
                if (project.Modules.Any(module => module.FormPart == form)) continue;
                result.Add(Lookup(form) ?? new UnopenedDocument(FormCodeText.WholeFile(form),
                    FormCodeText.Prefix(form).Length, () => { editors.EditCode(form); return Lookup(form); }));
            }
            foreach (var related in project.RelatedDocuments)
            {
                var open = Lookup(related);
                if (open is not null) { result.Add(open); continue; }
                if (related.AbsolutePath is not { } path || !File.Exists(path)) continue;
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var marked = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                    string text;
                    try { text = Vb6TextFile.Decode(bytes, Vb6TextEncoding.Utf8 with { Utf8Bom = marked }); }
                    catch (DecoderFallbackException)
                    {
                        text = Vb6TextFile.Decode(bytes, Vb6TextEncoding.Latin1 with { Utf8Bom = marked });
                    }
                    result.Add(new UnopenedDocument(text, 0,
                        () => { editors.EditRelatedDocument(related); return Lookup(related); }, isCode: false));
                }
                catch (IOException) { /* The editor reports the read error if this file is opened. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        return result;
    }

    internal sealed class UnopenedDocument(string text, int prefixLength, Func<ISearchableDocument?> open,
        bool isCode = true) : ISearchableDocument
    {
        public TextDocument Document { get; } = new(text);
        public int CaretOffset { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        private readonly IReadOnlyList<TextRegion> regions = isCode ? ReadOnlyRegions.Of(text, prefixLength) : [];
        public ISearchableDocument? Open() => open();
        public bool IsReadOnlyRegion(int offset, int length) => SnapshotReadOnlyRegions()(offset, length);
        public Func<int, int, bool> SnapshotReadOnlyRegions()
        {
            return (offset, length) => ReadOnlyRegions.Changes(regions, offset, length);
        }
    }
}

internal static class SearchableDocumentDockExtensions
{
    public static ISearchableDocument? FindSearchableDocument(this IDocumentDockService dock, object definition) =>
        dock.OpenDocuments.OfType<ISearchableDocument>()
        .FirstOrDefault(document => document switch
        {
            CodeEditorViewModel code => ReferenceEquals(code.ModuleDefinition, definition)
                || ReferenceEquals(code.FormDefinition, definition),
            RelatedDocumentEditorViewModel related => ReferenceEquals(related.RelatedDocument, definition),
            _ => false
        });
}
