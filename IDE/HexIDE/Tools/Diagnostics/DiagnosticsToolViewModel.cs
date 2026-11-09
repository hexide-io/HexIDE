using System.Collections.ObjectModel;
using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools.Diagnostics;

public sealed record DiagnosticRow(string Uri, string Server, Diagnostic Diagnostic, string SeverityText, string? DocumentName = null)
{
    public string FullPath => System.Uri.TryCreate(Uri, UriKind.Absolute, out var uri) && uri.IsFile
        ? uri.LocalPath : System.Uri.UnescapeDataString(Uri);
    public string Document => DocumentName ?? FullPath.Replace('\\', '/').Split('/').Last();
    public int Line => Diagnostic.Range.Start.Line + 1;
    public string Message => Diagnostic.Message;
    public string? Source => Diagnostic.Source;
    public double MessageOpacity => Diagnostic.Tags?.Contains(1) == true ? 0.5 : 1;
    public Avalonia.Media.TextDecorationCollection? MessageDecorations => Diagnostic.Tags?.Contains(2) == true
        ? Avalonia.Media.TextDecorations.Strikethrough : null;
    public DiagnosticSeverity Severity => Diagnostic.Severity ?? DiagnosticSeverity.Error;
}

public partial class DiagnosticsToolViewModel : Tool
{
    private readonly ILanguageConnectionRegistry registry;
    private readonly IDocumentDockService documents;
    private readonly IEditorService editors;
    private string? currentUri;
    private readonly ILocalizationService localization;

    public ObservableCollection<DiagnosticRow> Rows { get; } = [];
    [Notify] private int errorCount;
    [Notify] private int warningCount;
    [Notify] private int informationCount;
    [Notify] private int hintCount;
    [Notify] private bool errors = true;
    [Notify] private bool warnings = true;
    [Notify] private bool information = true;
    [Notify] private bool hints = true;
    [Notify] private bool currentDocumentOnly;
    [Notify] private DiagnosticRow? selectedRow;

    public DiagnosticsToolViewModel(ILanguageConnectionRegistry registry, ILspClient client,
        IDocumentDockService documents, IEditorService editors, ILocalizationService localization)
    {
        this.registry = registry;
        this.localization = localization;
        this.documents = documents;
        this.editors = editors;
        localization.BindTitle(this, "Str.Tool.Diagnostics.Title");
        CanClose = true;
        CanPin = false;
        localization.LanguageChanged += Refresh;
        client.DiagnosticsPublished += (_, _) => Dispatcher.UIThread.Post(Refresh);
        documents.PropertyChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        // Retain the last editor while the diagnostics panel itself has focus.
        var active = documents.ActiveDocument;
        if (active is CodeEditorViewModel { OpenDocument: not null } code) currentUri = code.GetDocumentUriPublic();
        else if (active is RelatedDocumentEditorViewModel { RelatedDocument.AbsolutePath: { } path })
            currentUri = LspDocumentUri.ForFile(path);
        else if (active?.OpenDocument is { } identity) currentUri = DocumentWireName.For(identity);

        var selected = SelectedRow;
        Rows.Clear();
        int errorsTotal = 0, warningsTotal = 0, informationTotal = 0, hintsTotal = 0;
        foreach (var item in registry.Diagnostics)
        {
            if (CurrentDocumentOnly && (currentUri is null || !LspDocumentUri.AreSame(currentUri, item.Uri))) continue;
            var severity = item.Diagnostic.Severity ?? DiagnosticSeverity.Error;
            switch (severity)
            {
                case DiagnosticSeverity.Error: errorsTotal++; break;
                case DiagnosticSeverity.Warning: warningsTotal++; break;
                case DiagnosticSeverity.Information: informationTotal++; break;
                case DiagnosticSeverity.Hint: hintsTotal++; break;
            }
            if (!(severity switch
                {
                    DiagnosticSeverity.Error => Errors,
                    DiagnosticSeverity.Warning => Warnings,
                    DiagnosticSeverity.Information => Information,
                    DiagnosticSeverity.Hint => Hints,
                    _ => true
                })) continue;
            var server = registry.Connections.FirstOrDefault(c => c.Id == item.Owner)?.DisplayName ?? item.Owner;
            Rows.Add(new DiagnosticRow(item.Uri, server, item.Diagnostic, localization.GetString(
                "Str.Tool.Diagnostics.Severity." + (severity switch
                {
                    DiagnosticSeverity.Warning => "Warning",
                    DiagnosticSeverity.Information => "Information",
                    DiagnosticSeverity.Hint => "Hint",
                    _ => "Error"
                })), editors.DocumentNameForUri(item.Uri)));
        }
        ErrorCount = errorsTotal;
        WarningCount = warningsTotal;
        InformationCount = informationTotal;
        HintCount = hintsTotal;
        SelectedRow = Rows.FirstOrDefault(r => r == selected);
    }

    public bool NavigateSelected() => SelectedRow is { } row
        && editors.NavigateTo(row.Uri, row.Line, row.Diagnostic.Range.Start.Character + 1);

    private void OnErrorsChanged() => Refresh();
    private void OnWarningsChanged() => Refresh();
    private void OnInformationChanged() => Refresh();
    private void OnHintsChanged() => Refresh();
    private void OnCurrentDocumentOnlyChanged() => Refresh();
}
