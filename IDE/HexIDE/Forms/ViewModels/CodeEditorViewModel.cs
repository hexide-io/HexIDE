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
    private readonly HexIDE.Debugging.IBreakpointService breakpointService;
    private readonly HexIDE.Runtime.Debugging.IDebugController debugController;
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
    public HexIDE.Debugging.IBreakpointService BreakpointService => breakpointService;
    public HexIDE.Runtime.Debugging.IDebugController DebugController => debugController;

    /// <summary>True while the project is running OR paused in the debugger — the window in which a code edit
    /// triggers the VB6 "reset your project?" prompt (the interpreter can't hot-patch a running program). Read off
    /// the debug controller (active between its run-start Reset and its run-end Stop) rather than
    /// IProjectRunnerService, which would close a DI cycle via the editor factory.</summary>
    public bool IsProjectRunning => debugController.IsSessionActive;

    /// <summary>VB6-faithful Edit-and-Continue affordance: the interpreter can't apply an edit to a running program
    /// live, so editing while running/paused pops VB6's own reset prompt. Yes → request a project reset (the edit
    /// then stands); No → the edit is left cancelled and the run continues. Returns true if a reset was requested.
    /// The reset goes through the event bus (ProjectRunnerService handles EndProjectRequestedEvent) to avoid a
    /// direct dependency on the runner, which would cycle.</summary>
    public async Task<bool> ConfirmResetWhileRunningAsync()
    {
        var result = await windowManager.MessageBox(
            localization.GetString("Str.ProjectRunner.EditWhileRunningConfirm"),
            buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning);
        if (result != MessageBoxResult.Yes)
            return false;
        eventBus.Publish(new EndProjectRequestedEvent());
        return true;
    }
    public FormDefinition? FormDefinition => formDefinition;
    public ModuleDefinition? ModuleDefinition => moduleDefinition;

    /// <summary>
    /// True when the underlying file cannot be written back faithfully, so a code edit would be discarded
    /// at save time.
    ///
    /// This covers a form's *code*, not just its layout: the code lives inside the .frm, so refusing to
    /// save the form discards code edits too. Gating only the designer would leave the more likely loss
    /// — someone typing a procedure — completely unprotected.
    /// </summary>
    public bool IsReadOnly => formDefinition is { CanSaveFaithfully: false };

    public string? ReadOnlyReason => formDefinition?.UnfaithfulSaveReason;

    public ObservableCollection<string> ObjectNames    { get; } = new();
    public ObservableCollection<string> ProcedureNames { get; } = new();

    [Notify] private string? selectedObject;
    [Notify] private string? selectedProcedure;

    private const string GeneralObject   = "(General)";
    private const string DeclarationsProc = "(Declarations)";

    private DocumentSymbol[]? _symbols;

    /// <summary>
    /// This document's conversation with the language layer, shared with the carried-file editor.
    ///
    /// <para>
    /// Created in <see cref="Initialize(FormDefinition)"/> rather than the constructor, because the URI it
    /// is named by is not knowable until a definition has been supplied — a constructor-time session would
    /// name every editor <c>vb6://form/untitled</c>.
    /// </para>
    /// </summary>
    private LspDocumentSession? session;

    public CodeEditorViewModel(IWindowManager windowManager,
        IEditorService editorService,
        IProjectService projectService,
        IEventBus eventBus,
        ILspClient lspClient,
        ISettingsService settingsService,
        IStatusBarService statusBarService,
        IBookmarkService bookmarkService,
        HexIDE.Debugging.IBreakpointService breakpointService,
        HexIDE.Runtime.Debugging.IDebugController debugController,
        ILocalizationService localization)
    {
        this.windowManager = windowManager;
        this.editorService = editorService;
        this.projectService = projectService;
        this.eventBus = eventBus;
        this.lspClient = lspClient;
        this.settingsService = settingsService;
        this.bookmarkService = bookmarkService;
        this.breakpointService = breakpointService;
        this.debugController = debugController;
        this.statusBarService = statusBarService;
        this.localization = localization;

        // Refresh the tab title (its "(Code)" suffix is localized) when the language changes. Unsubscribe on Dispose
        // (tab close) — a raw `+=` kept every closed code-editor VM (each holding a full document buffer) reachable
        // from the singleton localization service, so none were collected and a language switch replayed on all of them.
        Action onLanguageChanged = () => Title = ComputeTitle();
        localization.LanguageChanged += onLanguageChanged;
        AutoDispose(new ActionDisposable(() => localization.LanguageChanged -= onLanguageChanged));

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
        AutoDispose(this.eventBus.Subscribe<DocumentSavedEvent>(e =>
        {
            // Matched by reference against this editor's own document, as every other event here is.
            //
            // EITHER half matches, and that is safe rather than a duplication risk. A UserControl or
            // PropertyPage is one file whose two halves are both set by Initialize(ModuleDefinition)
            // (#152), so the event names both — but a save publishes ONE event, this handler runs once,
            // and the session's URI is fixed, so both halves matching produces exactly one announcement
            // under exactly one URI. Ordering the checks to prefer the module would read as though it
            // prevented something; it prevents nothing, and mutation testing says so.

            var mine = (e.Module is not null && ReferenceEquals(e.Module, moduleDefinition))
                    || (e.Form is not null && ReferenceEquals(e.Form, formDefinition));

            if (mine && session is { } open) open.NotifySavedAsync().ListenErrors();
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
            // Disposed from HERE rather than AutoDispose'd from Initialize. Dispose walks its
            // disposables in REVERSE registration order, so a session registered later would close the
            // document BEFORE the buffer above was written back to the definition.
            session?.Dispose();
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

        OpenToLanguageLayer();

        Title = ComputeTitle();
        return this;
    }

    public CodeEditorViewModel Initialize(ModuleDefinition moduleElement)
    {
        this.moduleDefinition = moduleElement;
        // BOTH halves, because a UserControl or PropertyPage HAS both and they are one file.
        //
        // This used to set only the module, while the designer's View Code went through
        // Initialize(FormDefinition) and set only the form part. Two tabs could then stand open over one
        // .ctl, each flushing to its own buffer — and the two save paths read different ones (SaveModule
        // serializes module.Code, SerializeFormToFile serializes formPart.Code). Code typed in one tab was
        // written by neither the other's save nor reported by IsDirty, which reads module.Code. It went
        // silently missing. (#152)
        //
        // Null for a .bas or .cls, which have no designer half — so everything downstream that tests
        // formDefinition stays correct for them without a kind check.
        this.formDefinition = moduleElement.FormPart;
        AutoDispose(moduleElement.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        AutoDispose(moduleElement.Owner.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        Document.Text = moduleElement.Code;

        // A module with a designer half lists its controls like a form's editor does; one without falls
        // back to "(General)" alone, which is what this used to hardcode.
        PopulateObjectNames();

        OpenToLanguageLayer();

        Title = ComputeTitle();
        return this;
    }

    /// <summary>
    /// Hands this document to the language layer.
    ///
    /// <para>
    /// Called from <c>Initialize</c>, after the definition is assigned and after <c>Document.Text</c> is
    /// loaded, and from nowhere else. Both are load-bearing: the URI is not knowable before the first, and
    /// starting before the second would open the document with an empty buffer <em>and</em> turn the load
    /// assignment itself into a spurious change notification, because the session hooks
    /// <c>TextChanged</c> as it starts.
    /// </para>
    /// </summary>
    private void OpenToLanguageLayer()
    {
        // GetDocumentUri(), not a form-or-module expression written out again: a UserControl or
        // PropertyPage sets BOTH definition fields (#152) and the module must win. One rule, one place.
        session = new LspDocumentSession(lspClient, Document, GetDocumentUri());

        // Forwarded into this class's own event rather than re-exposed as a pass-through. The view
        // subscribes once when it attaches and never replays, so a subscription that landed on the session
        // object instead would die with it — silently, and permanently blank.
        session.MarkersChanged += markers => MarkersChanged?.Invoke(markers);

        // The same piggyback as before: a fresh diagnostic set means the server has evidently just re-read
        // the document, which is the cheapest signal that its symbols are worth asking for again.
        session.DiagnosticsApplied += () => _ = RefreshSymbolsAsync();

        session.Start();
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
        // Selected before the early return, not after the loop: a .bas or .cls has no designer half and
        // takes that return, and it still needs "(General)" selected rather than left null.
        SelectedObject = GeneralObject;
        if (formDefinition is null) return;
        foreach (var component in formDefinition.Components)
        {
            var name = component.GetPropertyOrDefault(VBProperties.NameProperty);
            if (name is { Length: > 0 })
                ObjectNames.Add(name);
        }
    }

    /// <summary>
    /// Cancels any pending debounce and immediately syncs the current document text to the LSP server.
    /// Call this before requests that depend on the server having up-to-date source (e.g. signatureHelp).
    /// </summary>
    internal Task FlushDocumentAsync(CancellationToken ct = default)
        => session?.FlushAsync(ct) ?? Task.CompletedTask;

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