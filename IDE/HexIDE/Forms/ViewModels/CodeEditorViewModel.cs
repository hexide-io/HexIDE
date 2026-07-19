using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Controls;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;
using R3;
using Serilog;

namespace HexIDE.Forms.ViewModels;

public partial class CodeEditorViewModel : BaseEditorWindowViewModel
{
    private readonly IWindowManager windowManager;
    private readonly IEditorService editorService;
    private readonly IProjectService projectService;
    private readonly IEventBus eventBus;
    private readonly ILspClient lspClient;
    private readonly ISettingsService settingsService;
    private readonly IStatusBarService statusBarService;
    private readonly IBookmarkService bookmarkService;
    private readonly ILocalizationService localization;
    protected override string ComputeTitle()
    {
        var code = localization.GetString("Str.Document.CodeSuffix");
        return moduleDefinition is not null
            ? $"{moduleDefinition.Owner.Name} - {moduleDefinition.Name} ({code})"
            : $"{formDefinition?.Owner.Name} - {formDefinition?.Name} ({code})";
    }
    public override object? Icon { get; } = HexIDE.Utils.IconFactory.Themed("Geo.Code");

    private TextDocument document = new TextDocument();
    private FormDefinition? formDefinition;
    private ModuleDefinition? moduleDefinition;

    public TextDocument Document => document;

    [Notify] private int caretOffset;
    [Notify] private int selectionStart;
    [Notify] private int selectionLength;

    public event Action? FocusWindowRequest;
    public event Action<IReadOnlyList<LspMarker>>? MarkersChanged;

    public IEventBus EventBus => eventBus;
    public ISettingsService Settings => settingsService;
    public IStatusBarService StatusBar => statusBarService;
    public IBookmarkService BookmarkService => bookmarkService;
    public FormDefinition? FormDefinition => formDefinition;
    public ModuleDefinition? ModuleDefinition => moduleDefinition;

    public ObservableCollection<string> ObjectNames    { get; } = new();
    public ObservableCollection<string> ProcedureNames { get; } = new();

    [Notify] private string? selectedObject;
    [Notify] private string? selectedProcedure;

    private const string GeneralObject   = "(General)";
    private const string DeclarationsProc = "(Declarations)";

    private DocumentSymbol[]? _symbols;
    private int _documentVersion;
    private CancellationTokenSource? _debounce;

    public CodeEditorViewModel(IWindowManager windowManager,
        IEditorService editorService,
        IProjectService projectService,
        IEventBus eventBus,
        ILspClient lspClient,
        ISettingsService settingsService,
        IStatusBarService statusBarService,
        IBookmarkService bookmarkService,
        ILocalizationService localization)
    {
        this.windowManager = windowManager;
        this.editorService = editorService;
        this.projectService = projectService;
        this.eventBus = eventBus;
        this.lspClient = lspClient;
        this.settingsService = settingsService;
        this.bookmarkService = bookmarkService;
        this.statusBarService = statusBarService;
        this.localization = localization;

        // Refresh the tab title (its "(Code)" suffix is localized) when the language changes.
        localization.LanguageChanged += () => Title = ComputeTitle();

        lspClient.DiagnosticsPublished += OnDiagnosticsPublished;

        AutoDispose(this.eventBus.Subscribe<CreateOrNavigateToSubEvent>(e =>
        {
            if (e.Form == formDefinition)
            {
                Log.Debug("CodeEditorViewModel: Handling CreateOrNavigateToSubEvent({SubName}) in {FormName}",
                    e.Sub, formDefinition?.Name);
                var sub = Document.IndexOf($"Sub {e.Sub}", 0, Document.TextLength, StringComparison.OrdinalIgnoreCase);
                if (sub != -1)
                {
                    Log.Debug("CodeEditorViewModel: Found 'Sub {SubName}' at offset {Offset}, navigating", e.Sub, sub);
                    var nextNewLineIndex = Document.IndexOf("\n", sub, Document.TextLength - sub, StringComparison.OrdinalIgnoreCase);
                    CaretOffset = nextNewLineIndex == -1 ? sub : nextNewLineIndex + 1;
                }
                else
                {
                    Log.Debug("CodeEditorViewModel: 'Sub {SubName}' not found, creating event stub", e.Sub);
                    AddProcedureViewModel vm = new AddProcedureViewModel();
                    vm.IsPublic = true;
                    vm.IsSub = true;
                    vm.Name = e.Sub;
                    var code = vm.GenerateCode();
                    InsertAtEnd(code.beginCode, code.endCode);
                }
                FocusWindowRequest?.Invoke();
            }
        }));
        AutoDispose(this.eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(e =>
        {
            formDefinition?.UpdateCode(Document.Text);
            if (moduleDefinition is not null)
                moduleDefinition.UpdateCode(Document.Text);
        }));
        AutoDispose(this.eventBus.Subscribe<FormUnloadedEvent>(e =>
        {
            if (e.Form == formDefinition)
                RequestClose();
        }));
        AutoDispose(new ActionDisposable(() =>
        {
            formDefinition?.UpdateCode(Document.Text);
            if (moduleDefinition is not null)
                moduleDefinition.UpdateCode(Document.Text);
            lspClient.CloseDocumentAsync(GetDocumentUri()).ListenErrors();
            lspClient.DiagnosticsPublished -= OnDiagnosticsPublished;
        }));
    }

    public CodeEditorViewModel Initialize(FormDefinition formElement)
    {
        this.formDefinition = formElement;
        AutoDispose(formElement.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        AutoDispose(formElement.Owner.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        Document.Text = formElement.Code;

        PopulateObjectNames();

        if (lspClient.IsRunning)
            lspClient.OpenDocumentAsync(GetDocumentUri(), Document.Text).ListenErrors();

        Document.TextChanged += OnTextChanged;
        Title = ComputeTitle();
        return this;
    }

    public CodeEditorViewModel Initialize(ModuleDefinition moduleElement)
    {
        this.moduleDefinition = moduleElement;
        AutoDispose(moduleElement.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        AutoDispose(moduleElement.Owner.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        Document.Text = moduleElement.Code;

        // Modules have no form components — just "(General)" in the object combo
        ObjectNames.Clear();
        ObjectNames.Add(GeneralObject);
        SelectedObject = GeneralObject;

        if (lspClient.IsRunning)
            lspClient.OpenDocumentAsync(GetDocumentUri(), Document.Text).ListenErrors();

        Document.TextChanged += OnTextChanged;
        Title = ComputeTitle();
        return this;
    }

    /// <summary>
    /// Replaces the editor buffer with <paramref name="newCode"/> after the file watcher reloaded the
    /// underlying file from disk. Preserves the caret position best-effort. The <c>Document.Text</c>
    /// assignment raises <c>TextChanged</c>, which debounces a didChange to the LSP server so diagnostics
    /// refresh — no explicit LSP call is needed. Must be called on the UI thread.
    /// </summary>
    internal void ReloadFrom(string newCode)
    {
        if (string.Equals(Document.Text, newCode, StringComparison.Ordinal))
            return;
        var caret = CaretOffset;
        Document.Text = newCode;
        CaretOffset = Math.Clamp(caret, 0, Document.TextLength);
    }

    private void PopulateObjectNames()
    {
        ObjectNames.Clear();
        ObjectNames.Add(GeneralObject);
        if (formDefinition is null) return;
        foreach (var component in formDefinition.Components)
        {
            var name = component.GetPropertyOrDefault(VBProperties.NameProperty);
            if (name is { Length: > 0 })
                ObjectNames.Add(name);
        }
        SelectedObject = GeneralObject;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;
        var version = ++_documentVersion;
        var text = Document.Text;
        var uri = GetDocumentUri();
        Task.Delay(300, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested && lspClient.IsRunning)
                lspClient.ChangeDocumentAsync(uri, version, text).ListenErrors();
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>
    /// Cancels any pending debounce and immediately syncs the current document text to the LSP server.
    /// Call this before requests that depend on the server having up-to-date source (e.g. signatureHelp).
    /// </summary>
    internal async Task FlushDocumentAsync(CancellationToken ct = default)
    {
        _debounce?.Cancel();
        if (!lspClient.IsRunning) return;
        var version = ++_documentVersion;
        await lspClient.ChangeDocumentAsync(GetDocumentUri(), version, Document.Text, ct);
    }

    private void OnDiagnosticsPublished(object? sender, PublishDiagnosticsParams p)
    {
        if (formDefinition is null && moduleDefinition is null) return;
        if (p.Uri != GetDocumentUri())
            return;

        // TextDocument requires UI-thread access; post the whole conversion there.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var doc = Document;
            var markers = new List<LspMarker>(p.Diagnostics.Length);
            foreach (var diag in p.Diagnostics)
            {
                var startLine = diag.Range.Start.Line + 1;  // AvaloniaEdit is 1-based
                var startCol = diag.Range.Start.Character;
                var endLine = diag.Range.End.Line + 1;
                var endCol = diag.Range.End.Character;

                if (startLine < 1 || startLine > doc.LineCount) continue;

                var startOffset = doc.GetOffset(startLine, startCol + 1);
                var endOffset = endLine <= doc.LineCount
                    ? doc.GetOffset(endLine, Math.Max(endCol + 1, startCol + 2))
                    : startOffset + 1;

                endOffset = Math.Min(endOffset, doc.TextLength);
                if (startOffset >= endOffset) endOffset = Math.Min(startOffset + 1, doc.TextLength);

                bool isError = diag.Severity is null || diag.Severity == DiagnosticSeverity.Error;
                markers.Add(new LspMarker(startOffset, endOffset, isError, diag.Message));
            }

            MarkersChanged?.Invoke(markers);

            // Refresh procedure list after each diagnostics update (option 1: piggyback)
            _ = RefreshSymbolsAsync();
        });
    }

    private async Task RefreshSymbolsAsync()
    {
        if (!lspClient.IsRunning) return;
        var symbols = await lspClient.RequestDocumentSymbolsAsync(GetDocumentUri());
        _symbols = symbols;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshProcedureNames());
    }

    private void RefreshProcedureNames()
    {
        var current = SelectedProcedure;
        ProcedureNames.Clear();

        if (SelectedObject == GeneralObject || SelectedObject is null)
        {
            ProcedureNames.Add(DeclarationsProc);
            if (_symbols is not null)
                foreach (var s in _symbols)
                    if (s.Kind is SymbolKind.Method or SymbolKind.Function or SymbolKind.Property)
                        ProcedureNames.Add(s.Name);
        }
        else
        {
            // Show events for the selected form/control
            var component = FindComponent(SelectedObject);
            if (component is not null)
                foreach (var ev in component.BaseClass.Events)
                    ProcedureNames.Add(ev.Name);
        }

        SelectedProcedure = (current is not null && ProcedureNames.IndexOf(current) >= 0) ? current : null;
    }

    private ComponentInstance? FindComponent(string name)
    {
        if (formDefinition is null) return null;
        foreach (var c in formDefinition.Components)
            if (c.GetPropertyOrDefault(VBProperties.NameProperty) == name)
                return c;
        return null;
    }

    private void OnSelectedObjectChanged(string? oldValue, string? newValue)
    {
        RefreshProcedureNames();
    }

    private string GetDocumentUri()
    {
        if (moduleDefinition is not null)
            return $"vb6://module/{moduleDefinition.Name}";
        var name = formDefinition?.Name ?? "untitled";
        return $"vb6://form/{name}";
    }

    public void SaveForm() => SaveWithFormattingAsync(() => projectService.SaveForm(formDefinition!, false)).ListenErrors();
    public void SaveModule() => SaveWithFormattingAsync(() => projectService.SaveModule(moduleDefinition!, false)).ListenErrors();

    private async Task SaveWithFormattingAsync(Func<Task> saveAction)
    {
        if (settingsService.FormatOnSave)
            await ApplyFormattingToDocumentAsync();
        await saveAction();
    }

    /// <summary>
    /// Requests formatting from the LSP server and applies edits to the document.
    /// Called automatically before save (keyword casing + indentation).
    /// </summary>
    private async Task ApplyFormattingToDocumentAsync()
    {
        if (!lspClient.IsRunning) return;
        try
        {
            await FlushDocumentAsync();
            var edits = await RequestFormattingAsync();
            if (edits.Length == 0) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Sort edits in reverse order to preserve offsets
                var sorted = new List<TextEdit>(edits);
                sorted.Sort((a, b) =>
                {
                    int cmp = b.Range.Start.Line.CompareTo(a.Range.Start.Line);
                    return cmp != 0 ? cmp : b.Range.Start.Character.CompareTo(a.Range.Start.Character);
                });

                var doc = Document;
                doc.BeginUpdate();
                try
                {
                    foreach (var te in sorted)
                    {
                        var startLine = doc.GetLineByNumber(te.Range.Start.Line + 1);
                        var endLine   = doc.GetLineByNumber(te.Range.End.Line + 1);
                        int startOff  = Math.Min(startLine.Offset + te.Range.Start.Character, startLine.EndOffset);
                        int endOff    = Math.Min(endLine.Offset + te.Range.End.Character, endLine.EndOffset);
                        doc.Replace(startOff, endOff - startOff, te.NewText);
                    }
                }
                finally
                {
                    doc.EndUpdate();
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug("[save-format] {ErrorMessage}", ex.Message);
        }
    }

    public Task<HoverResult?> RequestHoverAsync(Position position, CancellationToken ct = default)
        => lspClient.RequestHoverAsync(GetDocumentUri(), position, ct);

    public Task<FoldingRange[]> RequestFoldingRangesAsync(CancellationToken ct = default)
        => lspClient.RequestFoldingRangesAsync(GetDocumentUri(), ct);

    public Task<CompletionItem[]> RequestCompletionAsync(Position position, CancellationToken ct = default)
        => lspClient.RequestCompletionAsync(GetDocumentUri(), position, ct);

    public Task<SignatureHelp?> RequestSignatureHelpAsync(Position position, CancellationToken ct = default)
        => lspClient.RequestSignatureHelpAsync(GetDocumentUri(), position, ct);

    public Task<Location[]?> RequestDefinitionAsync(Position position, CancellationToken ct = default)
        => lspClient.RequestDefinitionAsync(GetDocumentUri(), position, ct);

    public Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(Position position, CancellationToken ct = default)
        => lspClient.RequestDocumentHighlightAsync(GetDocumentUri(), position, ct);

    public Task<WorkspaceEdit?> RequestRenameAsync(Position position, string newName, CancellationToken ct = default)
        => lspClient.RequestRenameAsync(GetDocumentUri(), position, newName, ct);

    public Task<string?> ShowInputBoxAsync(string prompt, string title, string defaultText)
        => windowManager.InputBox(prompt, title, defaultText);

    public Task<TextEdit[]> RequestFormattingAsync(CancellationToken ct = default)
        => lspClient.RequestFormattingAsync(GetDocumentUri(), ct);

    /// <summary>Exposes the document URI for cross-file definition navigation.</summary>
    public string GetDocumentUriPublic() => GetDocumentUri();

    /// <summary>
    /// Opens the form or module matching the given LSP URI and navigates to the given line/column.
    /// Used for cross-file go-to-definition. Currently a no-op for cross-file (future enhancement).
    /// </summary>
    public void NavigateToUri(string uri, int line, int col)
    {
        // Cross-file navigation requires resolving a URI back to a FormDefinition/ModuleDefinition.
        // The LSP server currently only returns symbols from the same file, so this is a no-op for now.
        Log.Debug("[definition] Cross-file navigation to {Uri}:{Line}:{Col} is not yet implemented", uri, line, col);
    }

    private void OnSelectedProcedureChanged(string? oldValue, string? newValue)
    {
        if (newValue is null or DeclarationsProc) return;

        if (SelectedObject == GeneralObject || SelectedObject is null)
        {
            // Navigate to existing proc by symbol range
            if (_symbols is null) return;
            var sym = Array.Find(_symbols, s => s.Name == newValue);
            if (sym is null) return;
            var line = sym.SelectionRange.Start.Line + 1;
            if (line >= 1 && line <= Document.LineCount)
                CaretOffset = Document.GetLineByNumber(line).Offset;
        }
        else
        {
            // Event handler: find or generate Sub ObjectName_EventName(...)
            var handlerName = $"{SelectedObject}_{newValue}";
            var searchText  = $"Sub {handlerName}";
            var idx = Document.IndexOf(searchText, 0, Document.TextLength, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var line = Document.GetLineByOffset(idx);
                CaretOffset = line.EndOffset;
            }
            else
            {
                InsertEventHandlerStub(SelectedObject, newValue);
            }
        }

        FocusWindowRequest?.Invoke();
    }

    private void InsertEventHandlerStub(string objectName, string eventName)
    {
        var component = FindComponent(objectName);
        EventClass? ev = null;
        if (component is not null)
            foreach (var e in component.BaseClass.Events)
                if (e.Name == eventName) { ev = e; break; }

        var sb = new StringBuilder();
        sb.Append($"Private Sub {objectName}_{eventName}(");
        if (ev is not null)
        {
            for (int i = 0; i < ev.Arguments.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append($"{ev.Arguments[i].DefaultName} As {ev.Arguments[i].Type}");
            }
        }
        sb.AppendLine(")");
        sb.AppendLine();
        sb.Append($"End Sub");

        InsertAtEnd(sb.ToString(), string.Empty);
    }

    public void SaveFormAs() => SaveWithFormattingAsync(() => projectService.SaveForm(formDefinition!, true)).ListenErrors();

    public void ViewCode() => editorService.EditCode(formDefinition);

    public void ViewObject() => editorService.EditForm(formDefinition);

    public async Task AddProcedure()
    {
        var vm = new AddProcedureViewModel();
        if (!await windowManager.ShowDialog(vm))
            return;

        var code = vm.GenerateCode();
        InsertAtEnd(code.beginCode, code.endCode);
    }

    private void InsertAtEnd(string beginCode, string endCode)
    {
        var textLen = Document.TextLength;
        if (textLen >= 2)
        {
            var end = Document.GetText(textLen - 2, 2);
            if (end != "\n\n")
                Document.Insert(textLen, "\n\n");
            else if (end[1] == '\n')
                Document.Insert(textLen, "\n");
        }

        Document.Insert(Document.TextLength, beginCode);
        var offset = Document.TextLength;
        Document.Insert(Document.TextLength, endCode);
        CaretOffset = offset;
        FocusWindowRequest?.Invoke();
    }
}