using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public sealed record ReferenceRow(Location? Location, string Module, string Before, string Highlight, string After)
{
    public int? Line => Location?.Range.Start.Line + 1;
    public string AccessibleName { get; init; } = $"{Module}{Before}{Highlight}{After}";
}

public partial class FindReferencesToolViewModel : ResultsToolViewModel
{
    private readonly ILspClient client;
    private readonly IEditorService editors;
    private readonly IProjectManager projects;
    private readonly IDocumentDockService documents;
    private readonly ILocalizationService localization;
    private int generation;
    private ReferenceSearchResult result = new([], []);
    public ObservableCollection<ReferenceRow> Rows { get; } = [];
    [Notify] private ReferenceRow? selectedRow;

    public FindReferencesToolViewModel(ILspClient client, IEditorService editors,
        IProjectManager projects, IDocumentDockService documents, ILocalizationService localization)
    {
        this.client = client;
        this.editors = editors;
        this.projects = projects;
        this.documents = documents;
        this.localization = localization;
        localization.BindTitle(this, "Str.Tool.FindReferences.Title");
        localization.LanguageChanged += Refresh;
        CanClose = true;
        CanPin = false;
    }

    public async Task SearchAsync(string uri, Position position, CancellationToken ct = default)
    {
        var current = ++generation;
        Rows.Clear();
        RequestShow();
        ReferenceSearchResult answer;
        try { answer = await client.FindReferencesAsync(uri, position, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) { answer = new([], [new ReferenceSearchError("", ex.Message)]); }
        if (current != generation) return;
        result = answer;
        Refresh();
    }

    /// <summary>Used by code lenses carrying locations; never asks a language server.</summary>
    public void ShowLocations(IEnumerable<Location> locations)
    {
        ++generation;
        result = new(locations.Distinct().ToArray(), []);
        Refresh();
        RequestShow();
    }

    private IEnumerable<DocumentIdentity> Members() => projects.LoadedProjects.SelectMany(p =>
        p.Modules.Select(DocumentIdentity.For).Concat(p.Forms.Select(DocumentIdentity.For))).Distinct();

    private void Refresh()
    {
        SelectedRow = null;
        Rows.Clear();
        var members = Members().ToArray();
        var groups = result.Locations.Distinct().GroupBy(l => l.Uri)
            .OrderBy(g => Array.FindIndex(members, m => LspDocumentUri.AreSame(DocumentWireName.For(m), g.Key)) is var index && index >= 0 ? index : int.MaxValue);
        foreach (var group in groups)
        {
            var member = members.FirstOrDefault(m => LspDocumentUri.AreSame(DocumentWireName.For(m), group.Key));
            var name = member?.Name ?? editors.DocumentNameForUri(group.Key) ?? group.Key;
            Rows.Add(new(null, name, "", "", ""));
            var open = documents.OpenDocuments.OfType<CodeEditorViewModel>()
                .FirstOrDefault(e => LspDocumentUri.AreSame(e.GetDocumentUriPublic(), group.Key));
            var text = open?.Document.Text ?? (member?.Module is { } module ? FormCodeText.Prefix(module) + module.Code
                : member?.Form is { } form ? FormCodeText.Prefix(form) + form.Code : "");
            var document = new TextDocument(text);
            foreach (var location in group.OrderBy(l => l.Range.Start.Line).ThenBy(l => l.Range.Start.Character))
            {
                var range = location.Range;
                var line = range.Start.Line >= 0 && range.Start.Line < document.LineCount
                    ? document.GetText(document.GetLineByNumber(range.Start.Line + 1)) : "";
                var start = Math.Clamp(range.Start.Character, 0, line.Length);
                var end = range.End.Line > range.Start.Line ? line.Length : Math.Clamp(range.End.Character, start, line.Length);
                Rows.Add(new(location, "", line[..start], line[start..end], line[end..])
                    { AccessibleName = $"{name} {range.Start.Line + 1} {line}" });
            }
        }
        foreach (var error in result.Errors)
            Rows.Add(new(null, "", string.Format(localization.GetString("Str.Tool.FindReferences.Error"), error.Server, error.Message), "", ""));
        if (Rows.Count == 0)
            Rows.Add(new(null, "", localization.GetString("Str.Tool.FindReferences.Empty"), "", ""));
    }

    public bool NavigateSelected() => SelectedRow?.Location is { } location
        && editors.NavigateTo(location.Uri, location.Range.Start.Line + 1, location.Range.Start.Character + 1);
}
