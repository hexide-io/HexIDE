using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
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
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;
using R3;
using Serilog;

namespace HexIDE.Forms.ViewModels;

public partial class CodeEditorViewModel : BaseEditorWindowViewModel, ISearchableDocument
{
    private readonly IWindowManager windowManager;
    private readonly HexIDE.Debugging.IRunScope runScope;
    private readonly IEditorService editorService;
    private readonly IProjectService projectService;
    private readonly IEventBus eventBus;
    private readonly ILspClient lspClient;
    private readonly ISettingsService settingsService;
    private readonly IStatusBarService statusBarService;
    private readonly IBookmarkService bookmarkService;
    private readonly HexIDE.Debugging.IBreakpointService breakpointService;
    private readonly HexIDE.Runtime.Debugging.IDebugController debugController;
    public HexIDE.Debugging.ExternalDebugService? ExternalDebugger { get; }
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

    // Per code window, deliberately absent from project and settings serialization (#1113).
    [Notify] private int conditionalTargetIndex;

    [Notify] private int caretOffset;
    [Notify] private int selectionStart;
    [Notify] private int selectionLength;

    public event Action? FocusWindowRequest;
    public event Action<IReadOnlyList<LspMarker>>? MarkersChanged;

    /// <summary>
    /// A language server for this document came up, or went away. Raised on the UI thread.
    /// </summary>
    /// <remarks>
    /// <b>Exists for folding, which has no other signal.</b> The view asks for folding ranges when it
    /// attaches, and on the first document of a session that is before any server has answered
    /// <c>initialize</c> — the registry then has no started claimant advertising the capability, answers
    /// with an empty set, and nothing asks again until the text changes. So a module opened and read
    /// without being typed into never folded at all (hexide-io/HexIDE#446).
    ///
    /// <para>
    /// Deliberately the server's state rather than a published diagnostic, which is what the carried-file
    /// editor keys on. A server may advertise <c>foldingRangeProvider</c> and publish nothing — it is
    /// entitled to — and on that server a publish would never arrive to hang the retry on.
    /// </para>
    /// </remarks>
    public event Action? LanguageServerStateChanged;

    /// <summary>Held so the subscription can be dropped, and so it is only ever taken once.</summary>
    private EventHandler? languageServerStateHandler;

    /// <summary>
    /// The most recent diagnostics, kept so a view attaching later can catch up.
    ///
    /// <para>
    /// <see cref="MarkersChanged"/> is a notification, not a state: a view that subscribes after the last
    /// publication sees nothing until the next one. That is not hypothetical — moving this document to
    /// another dock re-materialises the view, and neither source of diagnostics has any reason to publish
    /// again for a document that has not changed, so the squiggles would simply not come back
    /// (hexide-io/HexIDE#270).
    /// </para>
    ///
    /// <para>
    /// It matters more here than in the carried-file editor, because a second source feeds this channel:
    /// compiling with the real VB6 toolchain injects its errors through it, and a build is a much rarer
    /// event to have to wait for again than a keystroke.
    /// </para>
    /// </summary>
    public IReadOnlyList<LspMarker> Markers { get; private set; } = [];

    public IEventBus EventBus => eventBus;
    public ISettingsService Settings => settingsService;
    public IStatusBarService StatusBar => statusBarService;
    public IBookmarkService BookmarkService => bookmarkService;
    public HexIDE.Debugging.IBreakpointService BreakpointService => breakpointService;
    public HexIDE.Runtime.Debugging.IDebugController DebugController => debugController;
    public HexIDE.Tools.CallStackToolViewModel? CallStack { get; }

    /// <summary>True while the project is running OR paused in the debugger — the window in which a code edit
    /// triggers the VB6 "reset your project?" prompt (the interpreter can't hot-patch a running program). Read off
    /// the debug controller (active between its run-start Reset and its run-end Stop) rather than
    /// IProjectRunnerService, which would close a DI cycle via the editor factory.</summary>
    public bool IsProjectRunning => debugController.IsSessionActive || ExternalDebugger?.IsActive == true;

    /// <summary>VB6-faithful Edit-and-Continue affordance: the interpreter can't apply an edit to a running program
    /// live, so editing while running/paused pops VB6's own reset prompt. OK → request a project reset (the edit
    /// then stands); Cancel → the edit is left cancelled and the run continues. Returns true if a reset was requested.
    /// The reset goes through the event bus (ProjectRunnerService handles EndProjectRequestedEvent) to avoid a
    /// direct dependency on the runner, which would cycle.</summary>
    /// <param name="over">The visible editor window to show the prompt over. The prompt holds all desktop windows.</param>
    public async Task<bool> ConfirmResetWhileRunningAsync(Avalonia.Controls.Window? over = null)
    {
        var result = await windowManager.TaskModalMessageBox(
            localization.GetString("Str.ProjectRunner.EditWhileRunningConfirm"),
            buttons: MessageBoxButtons.OkCancel, icon: MessageBoxIcon.Warning, owner: over);
        if (result != MessageBoxResult.Ok)
            return false;
        eventBus.Publish(new EndProjectRequestedEvent());
        return true;
    }
    public FormDefinition? FormDefinition => formDefinition;
    public ModuleDefinition? ModuleDefinition => moduleDefinition;

    /// <summary>
    /// What this editor's document <em>is</em> — the key everything per-document hangs on.
    /// </summary>
    /// <remarks>
    /// Fixed when the editor is initialized and never recomputed, because the definition it names never
    /// changes: a rename, a first save or a Save As all leave the same object in place. The gutters are built
    /// from this and the F9 and Ctrl+F2 commands read the same value, which is what stops the two
    /// disagreeing after a rename (#269).
    /// </remarks>
    public DocumentIdentity Identity =>
        OpenDocument ?? throw new InvalidOperationException(
            "A code editor has no document until Initialize has been called with a form or a module.");

    /// <inheritdoc/>
    public override DocumentIdentity? OpenDocument => identity;

    private DocumentIdentity? identity;

    /// <summary>The project currently running, or null. Read live, never captured.</summary>
    public ProjectDefinition? RunningProject => runScope.RunningProject;

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
    /// is named by is not knowable until a definition has been supplied: <see cref="DocumentWireName"/> works it
    /// out from the document's identity, its <c>file:</c> URI or <c>untitled:&lt;Project&gt;/&lt;Name&gt;.&lt;ext&gt;</c>,
    /// and a constructor-time session would have neither a file nor a name to give it. (The retired
    /// <c>vb6://</c> scheme would have named every such editor <c>vb6://form/untitled</c>.)
    /// </para>
    /// </summary>
    private LspDocumentSession? session;
    private readonly HexIDE.Tools.RecentEditHistory? recentEditHistory;

    /// <summary>Whether this editor's text has diagnostics yet; see <see cref="LspDocumentSession.AwaitingDiagnostics"/>.</summary>
    public bool AwaitingDiagnostics => session?.AwaitingDiagnostics ?? false;

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
        HexIDE.Debugging.IRunScope runScope,
        ILocalizationService localization,
        HexIDE.Debugging.ExternalDebugService? externalDebugger = null,
        HexIDE.Tools.CallStackToolViewModel? callStack = null,
        HexIDE.Tools.RecentEditHistory? recentEditHistory = null)
    {
        this.recentEditHistory = recentEditHistory;
        CanFloat = false;
        this.windowManager = windowManager;
        this.editorService = editorService;
        this.projectService = projectService;
        this.eventBus = eventBus;
        this.lspClient = lspClient;
        this.settingsService = settingsService;
        this.bookmarkService = bookmarkService;
        this.breakpointService = breakpointService;
        this.debugController = debugController;
        ExternalDebugger = externalDebugger;
        CallStack = callStack;
        this.runScope = runScope;
        this.statusBarService = statusBarService;
        this.localization = localization;
        Document.TextChanged += (_, _) => RefreshBufferModified();

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
                var headerEnd = ReadOnlyRegions.HeaderEnd(Document.Text, bufferPrefix.Length);
                var sub = Document.IndexOf($"Sub {e.Sub}", headerEnd, Document.TextLength - headerEnd, StringComparison.OrdinalIgnoreCase);
                if (sub != -1)
                {
                    Log.Debug("CodeEditorViewModel: Found 'Sub {SubName}' at offset {Offset}, navigating", e.Sub, sub);
                    var nextNewLineIndex = Document.IndexOf("\n", sub, Document.TextLength - sub, StringComparison.OrdinalIgnoreCase);
                    CaretOffset = nextNewLineIndex == -1 ? sub : nextNewLineIndex + 1;
                }
                else
                {
                    Log.Debug("CodeEditorViewModel: 'Sub {SubName}' not found, creating event stub", e.Sub);
                    // Private, as VB6 writes an event procedure (measured, #1532). Add Procedure's own default is Public.
                    AddProcedureViewModel vm = new AddProcedureViewModel();
                    vm.IsPublic = false;
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
            // Marshalled, because failing here is INVISIBLE and corrupting. Document.Text throws
            // "Call from invalid thread" off the UI thread, EventBus logs the exception and moves to the
            // next handler, and the save that published this event then serializes the model's previous
            // code as though the editor had been flushed. The caller is told the file was written; it was,
            // with the wrong contents. An MCP write tool did exactly this. (#334)
            //
            // Invoke rather than Post: the publisher writes the model to disk on the very next statement,
            // so a flush that has merely been queued is a flush that did not happen. Inlines when already
            // on the UI thread, which every in-app caller is.
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                Flush();
            else
                Avalonia.Threading.Dispatcher.UIThread.Invoke(Flush);

            void Flush()
            {
                // Split at the prefix, not the whole buffer: the model keeps holding the code section, and
                // writing the composed text back would put the designer block into Code, where the
                // interpreter compiles it as VB.
                var body = BufferBody;
                formDefinition?.UpdateCode(body);
                if (moduleDefinition is not null)
                    moduleDefinition.UpdateCode(body);
                RefreshModelModified();
            }
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

            if (mine) RefreshModelModified();

            // A save is also the commonest way a document's NAME changes: a first save gives it a file,
            // and saving a project into another directory repoints every one of them. So reconcile the
            // session's name before announcing the save, and announce the save under the new name -- the
            // other order tells a server about a write to a document it has just been told to forget.
            if (mine && session is { } open) ReconcileThenAnnounceSaveAsync(open).ListenErrors();
        }));

        AutoDispose(this.eventBus.Subscribe<ModuleUnloadedEvent>(e =>
        {
            if (e.Module == moduleDefinition)
                RequestClose();
        }));
        AutoDispose(this.eventBus.Subscribe<FormUnloadedEvent>(e =>
        {
            if (e.Form == formDefinition)
                RequestClose();
        }));
        AutoDispose(new ActionDisposable(() =>
        {
            var body = BufferBody;   // the same split as the flush above, for the same reason
            formDefinition?.UpdateCode(body);
            if (moduleDefinition is not null)
                moduleDefinition.UpdateCode(body);
            // Disposed from HERE rather than AutoDispose'd from Initialize. Dispose walks its
            // disposables in REVERSE registration order, so a session registered later would close the
            // document BEFORE the buffer above was written back to the definition.
            session?.Dispose();
        }));
    }

    private bool modelIsModified;

    private void RefreshBufferModified() => IsModified = modelIsModified ||
        !string.Equals(BufferBody, moduleDefinition?.Code ?? formDefinition?.Code ?? "", StringComparison.Ordinal);

    private void RefreshModelModified()
    {
        modelIsModified = moduleDefinition is { } module ? projectService.HasUnsavedChanges(module)
            : formDefinition is { } form && projectService.HasUnsavedChanges(form);
        RefreshBufferModified();
    }

    /// <summary>
    /// The text this buffer carries in front of the document's code, and splits at on flush.
    /// </summary>
    /// <remarks>
    /// <b>Stored rather than recomputed, because the buffer's prefix and the model's diverge.</b> Renaming
    /// a module changes the header the model would render — <c>Attribute VB_Name = "Utilities"</c> is
    /// longer than <c>= "Mod1"</c> — while the buffer still holds the old one until something replaces
    /// both together. Splitting at the model's current length would then cut into the body.
    /// </remarks>
    private string bufferPrefix = "";

    /// <summary>
    /// Set while an undo is replaying one of the IDE's own writes — a header write or a <c>VB_Name</c> write
    /// — so the caller of <see cref="UndoRequested"/> can tell what it just undid. Reset by that caller
    /// before each pop; nothing else reads it.
    /// </summary>
    private bool ownerWriteWasUndone;

    /// <summary>
    /// The header the MODEL would show now — which is not always the one the buffer carries, and the
    /// difference is the point: after an undo the buffer holds the previous header and this holds the
    /// current one, so this is what gets put back.
    /// </summary>
    private string ModelHeader =>
        moduleDefinition is { } module ? FormCodeText.Prefix(module)
        : formDefinition is { } form ? FormCodeText.Prefix(form)
        : bufferPrefix;

    /// <summary>
    /// The half of a header write that AvaloniaEdit cannot undo for us: <see cref="bufferPrefix"/>, which
    /// says where the header ends and is not part of the document.
    /// </summary>
    /// <remarks>
    /// <b>Pushed into the same undo group as the text change, which is what makes the pair atomic.</b> The
    /// buffer is "header + code" and the split is by the prefix's LENGTH, so a text change that moves the
    /// header without moving the prefix does not merely look wrong — the next flush takes the body from the
    /// wrong offset and writes a truncated document. Measured before this existed: type <c>Dim x As Long</c>,
    /// grow the header, press Ctrl+Z, and the body came back as <c>x As Long</c>.
    ///
    /// <para>
    /// <b>Why an operation rather than bookkeeping.</b> <c>UndoStack.LastGroupDescriptor</c> looks like the
    /// way to ask "is the top of the stack the IDE's own write", and it is not: it reports the last group
    /// <em>opened</em>, an <c>Undo()</c> clears it to null even when marked groups remain below, and it is
    /// already null inside <c>Changed</c> during the replay (all measured against AvaloniaEdit 12.0.0). An
    /// operation inside the group is told when that group is undone, at any depth, whatever route the undo
    /// came in by — including the Ctrl+Y that AvaloniaEdit handles itself and HexIDE never sees.
    /// </para>
    ///
    /// <para>
    /// It is pushed BEFORE the text change, so on undo it runs after it (a group replays its operations in
    /// reverse) and on redo before it. The prefix is therefore correct at every point an observer could
    /// look, in both directions.
    /// </para>
    /// </remarks>
    private sealed class HeaderWrite(CodeEditorViewModel owner, string before, string after)
        : IUndoableOperation
    {
        public void Undo()
        {
            owner.bufferPrefix = before;
            owner.ownerWriteWasUndone = true;
        }

        public void Redo() => owner.bufferPrefix = after;
    }

    /// <summary>
    /// Marks a <c>VB_Name</c> write as the IDE's own, so <see cref="UndoRequested"/> treats it exactly as it
    /// treats a header write.
    /// </summary>
    /// <remarks>
    /// It carries no state because the write it marks needs none restored: the line is in the body, below
    /// the prefix, so the split does not move. What it is for is telling the undo loop that the entry it
    /// just popped was not an edit the developer made here.
    /// </remarks>
    private sealed class NameWrite(CodeEditorViewModel owner) : IUndoableOperation
    {
        public void Undo() => owner.ownerWriteWasUndone = true;

        public void Redo() { }
    }

    /// <summary>
    /// Marks a save's rewrite of members' attribute qualifiers as the IDE's own (#273 task 4.4), so
    /// <see cref="UndoRequested"/> treats it as it treats a header write, and puts it back afterwards.
    /// </summary>
    /// <remarks>
    /// Stateless for the reason <see cref="NameWrite"/> is: the qualifiers are in the body, so the split does not
    /// move. What it adds is <see cref="qualifierWriteWasUndone"/>, because putting the qualifiers back is not
    /// wanted after every undo of the IDE's writes, only after one that undid them.
    /// </remarks>
    private sealed class QualifierWrite(CodeEditorViewModel owner) : IUndoableOperation
    {
        public void Undo()
        {
            owner.ownerWriteWasUndone = true;
            owner.qualifierWriteWasUndone = true;
        }

        public void Redo() { }
    }

    /// <summary>
    /// Set while an undo replays a save's qualifier rewrite (<see cref="QualifierWrite"/>), so
    /// <see cref="UndoRequested"/> knows to rewrite them again once the developer's edit is undone. Reset by that
    /// caller before its loop; nothing else reads it.
    /// </summary>
    private bool qualifierWriteWasUndone;

    /// <summary>
    /// Makes this buffer's attribute qualifiers the ones a save just wrote into the document's code,
    /// <paramref name="written"/> (#273 task 4.4), as one of the IDE's own writes. Does nothing when the buffer's
    /// runs already name their members, or when the save did not rewrite them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The save decides, and this follows it.</b> A save rewrites the model's qualifiers only where VB6 would
    /// write the module: one with unsaved changes, one saved by its own Save or Save As, or one written to a file
    /// other than its own. So a buffer whose runs still name a stale member may be exactly what a project's save
    /// of an unchanged member wrote. The rewrite is therefore made here only when it turns this buffer's code into
    /// <paramref name="written"/>, which is the save having made it too. Anything else, including a buffer that
    /// somehow no longer holds what was flushed, is left alone and logged: the next flush makes the buffer win,
    /// as it always does.
    /// </para>
    /// <para>
    /// <b>Replaced span by span, rather than by assigning the body</b>, for <see cref="RefreshPrefix"/>'s reason:
    /// the caret, the selection and the folds over the rest of the buffer stay where they were. Each span is in a
    /// member's read-only run, which the developer can neither type into nor have the caret inside, and which a
    /// fold usually hides. The view model's own <see cref="CaretOffset"/> is moved with the text, as there.
    /// </para>
    /// </remarks>
    internal void AdoptRewrittenQualifiers(string written)
    {
        var text = Document.Text;
        var rewrites = MemberAttributeQualifiers.Rewrites(text, bufferPrefix.Length);
        if (rewrites.Count == 0)
            return;
        if (!string.Equals(FormCodeText.BodyOf(MemberAttributeQualifiers.Apply(text, rewrites), bufferPrefix), written,
                StringComparison.Ordinal))
        {
            Log.Debug("{Document}: the save did not rewrite the attribute qualifiers this window holds, so they stand",
                identity?.Display);
            return;
        }
        WriteQualifiers(rewrites);
    }

    /// <summary>Makes <paramref name="rewrites"/>, in order of offset, as one of the IDE's own writes.</summary>
    private void WriteQualifiers(IReadOnlyList<TextChange> rewrites)
    {
        var caret = CaretOffset;
        Document.BeginUpdate();
        try
        {
            Document.UndoStack.Push(new QualifierWrite(this));
            for (var i = rewrites.Count - 1; i >= 0; i--)
                Document.Replace(rewrites[i].Offset, rewrites[i].Length, rewrites[i].Text);
        }
        finally
        {
            Document.EndUpdate();
        }

        var moved = 0;
        foreach (var rewrite in rewrites)
        {
            if (rewrite.Offset + rewrite.Length <= caret)
                moved += rewrite.Text.Length - rewrite.Length;
        }
        if (moved != 0)
            CaretOffset = Math.Clamp(caret + moved, 0, Document.TextLength);
    }

    /// <summary>
    /// The document's code, without the header the buffer shows in front of it.
    /// </summary>
    /// <remarks>
    /// <b>Every reader that wants "the code" goes through this, and every writer through
    /// <see cref="ReplaceBody"/>.</b> Since #273 task 3.2 the buffer is the whole file, so the two are no
    /// longer the same string, and a caller that assigns <c>Document.Text</c> a bare body would silently
    /// destroy the header — the next flush splits at the prefix's length and would take the first lines of
    /// the body with it.
    /// </remarks>
    public string BufferBody => FormCodeText.BodyOf(Document.Text, bufferPrefix);

    /// <summary>Replaces the code, leaving the header the buffer shows in front of it untouched.</summary>
    public void ReplaceBody(string body) => Document.Text = bufferPrefix + body;

    /// <summary>
    /// Puts the code back to <paramref name="body"/>, under whatever header the buffer carries now: what
    /// answering Cancel to Edit-and-Continue's reset prompt does (hexide-io/HexIDE#273 task 3.13).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The code, never the whole buffer.</b> The revert used to restore a snapshot of the whole buffer taken
    /// when the prompt opened. A designer commit or a save landing while the prompt was open had by then
    /// replaced the header, and <see cref="bufferPrefix"/> with it, so the snapshot put the old header back
    /// under the new prefix. The split is by the prefix's length, so the next flush took the code from the
    /// wrong offset — the header/prefix split task 3.8 closed on every other path. Restoring only the code
    /// keeps the header the IDE wrote, and the prefix that describes it.
    /// </para>
    /// <para>
    /// <b>Only the span that differs is replaced</b>, so the caret, the selection and the folds outside the
    /// developer's edit stay where they were rather than collapsing as a whole-text assignment collapses them.
    /// The revert is an ordinary undo entry, as the snapshot assignment was before it.
    /// </para>
    /// </remarks>
    internal void RevertBodyTo(string body)
    {
        var current = BufferBody;
        if (string.Equals(current, body, StringComparison.Ordinal))
            return;

        var head = 0;
        var shorter = Math.Min(current.Length, body.Length);
        while (head < shorter && current[head] == body[head])
            head++;
        var tail = 0;
        while (tail < shorter - head && current[^(tail + 1)] == body[^(tail + 1)])
            tail++;

        Document.Replace(bufferPrefix.Length + head, current.Length - head - tail, body[head..^tail]);
    }

    /// <summary>Where the code starts in the buffer: the length of the header it was composed with.</summary>
    internal int BufferPrefixLength => bufferPrefix.Length;

    /// <summary>The buffer as every line number for this window counts it: from the top of the file.</summary>
    public CodeWindowText CodeWindowText => new(Document.Text, bufferPrefix.Length);

    /// <summary>
    /// True once the developer has expanded the header's fold in this window, and false again once they fold
    /// it (#273 task 3.14).
    /// </summary>
    /// <remarks>
    /// Held here rather than by the view, because the view is not the window: a dock move re-materialises the
    /// view around this view model, and the fold has to come back the way the developer left it. The view
    /// reads it when it re-creates the fold and writes it back from the fold before each re-creation.
    /// </remarks>
    internal bool HeaderFoldExpanded { get; set; }

    /// <summary>
    /// The member attribute runs the developer has expanded in this window, by key (#273 task 4.3).
    /// </summary>
    /// <remarks>
    /// For the reason <see cref="HeaderFoldExpanded"/> is held here, and by key rather than by offset, because what
    /// it has to outlive is the fold itself: a reload, a format that rewrites a run's surroundings, or a dock move
    /// removes the fold, and the run's fold made again afterwards is folded unless its key is in this set. A key the
    /// window no longer shows is kept, so a run deleted and brought back by Undo comes back as it was left, unless
    /// another run of the same member took the key meanwhile, by an ordinal shift; and a key a shown run moves off,
    /// renamed or renumbered, goes, so a name a retype passes through is not left in the set to open some other
    /// member's run later (task 4.3 review). The view keeps both rules; design.md, "Settled while building 4.3".
    /// </remarks>
    internal HashSet<MemberRunKey> ExpandedMemberRuns { get; } = [];

    /// <summary>
    /// Toggles a breakpoint on the 1-based <paramref name="line"/>, unless it would set one on a read-only line.
    /// </summary>
    /// <returns>False when nothing changed because the line is read-only; the status bar then says why.</returns>
    /// <remarks>
    /// The one way the code window toggles a breakpoint: F9, Debug ▸ Toggle Breakpoint and a gutter click all
    /// come here (#273 task 3.12). A breakpoint already on such a line can still be removed. The sidecar's load
    /// drops one (task 3.17), but an edit or a reload can still leave one there, because a breakpoint is a bare
    /// line number: text that moves under it, or a marked line that becomes part of the header or of a member's
    /// attribute run, puts it on a read-only line (debugger-vb6-divergences.md, D24).
    /// </remarks>
    public bool ToggleBreakpoint(int line)
    {
        if (ExternalDebugger?.IsActive == true)
        {
            if (!ExternalDebugger.GetBreakpoints(GetDocumentUriPublic()).Any(row => row.RequestedLine == line) && RefuseMarkOn(line))
                return false;
            if (ExternalDebugger.ToggleBreakpoint(GetDocumentUriPublic(), line)) return true;
            statusBarService.SetTemporaryMessage(localization.GetString("Str.Debug.External.Unsupported"), TimeSpan.FromSeconds(6));
            return false;
        }
        if (!breakpointService.IsBreakpoint(Identity, line) && RefuseMarkOn(line))
            return false;
        breakpointService.Toggle(Identity, line);
        return true;
    }

    public string? DescribeExternalBreakpoint(int line)
    {
        if (ExternalDebugger?.IsActive != true) return null;
        var row = ExternalDebugger.GetBreakpoints(GetDocumentUriPublic()).FirstOrDefault(item => item.RequestedLine == line);
        if (row is null) return null;
        return string.Format(localization.GetString("Str.Debug.External.Breakpoint"), row.RequestedLine,
            row.Line?.ToString() ?? "—", row.Pending ? "…" : row.Verified ? "✓" : "×",
            row.Pending ? localization.GetString("Str.Debug.External.BreakpointPending") : row.Message);
    }

    /// <summary>
    /// Toggles a bookmark on the 1-based <paramref name="line"/>, unless it would set one on a read-only line.
    /// </summary>
    /// <returns>False when nothing changed because the line is read-only; the status bar then says why.</returns>
    /// <remarks>
    /// Ctrl+F2 and a click in the bookmark gutter both come here. Taken 1-based like every other line number the
    /// window gives out; the store's own 0-based numbering is converted here and nowhere else.
    /// </remarks>
    public bool ToggleBookmark(int line)
    {
        if (!bookmarkService.IsBookmarked(Identity, line - 1) && RefuseMarkOn(line))
            return false;
        bookmarkService.Toggle(Identity, line - 1);
        return true;
    }

    /// <summary>
    /// True, after telling the developer why, when the 1-based <paramref name="line"/> may not take a new mark.
    /// </summary>
    /// <remarks>
    /// A read-only line never executes, so a breakpoint there is never hit; and a folded header is one visible
    /// line standing for many, so a click on it would mark whichever of them it happened to map to, out of
    /// sight. The click maps to the fold's first line, which is the header's, so the fold needs no case of its
    /// own. The test is membership, not <see cref="IsReadOnlyRegion"/>: that one also refuses the line break
    /// a member's attribute run hangs from, which would put every described declaration out of reach.
    /// </remarks>
    private bool RefuseMarkOn(int line)
    {
        if (!CodeWindowText.IsReadOnlyLine(line))
            return false;
        statusBarService.SetTemporaryMessage(
            string.Format(localization.GetString("Str.CodeEditor.Msg.NoMarkOnReadOnlyLine"), line),
            TimeSpan.FromSeconds(6));
        return true;
    }

    /// <summary>
    /// Replaces the header the buffer shows in front of the code with a freshly-rendered one, leaving the
    /// code untouched. Does nothing when the two are the same string.
    /// </summary>
    /// <remarks>
    /// <b>Replaces the REGION, rather than assigning <see cref="Document"/>.Text.</b> A whole-document
    /// assignment collapses every anchor AvaloniaEdit holds over the buffer -- the caret, the selection,
    /// the folding sections a server sent -- so the developer's cursor would jump to the top of the file on
    /// every nudge of a control in the designer. Replacing the first <c>bufferPrefix.Length</c> characters
    /// moves everything below it by the difference instead, which is what actually happened.
    ///
    /// <para>
    /// <b>The diagnostic markers are NOT among them, and this used to claim they were.</b> An
    /// <c>LspMarker</c> is a plain record struct of two offsets in a list, not an anchored segment, so a
    /// header that changes height leaves every underline drawn against stale offsets until the next
    /// <c>publishDiagnostics</c> arrives -- which it does, because the replace debounces a <c>didChange</c>.
    /// Transient and self-healing, and hexide-io/HexIDE#512 covers making it not happen at all.
    /// </para>
    ///
    /// <para>
    /// The region is taken by the prefix's LENGTH, exactly as <see cref="BufferBody"/> splits, so the two
    /// cannot disagree about where the header ends. Since #273 task 3.7 the header is read-only to the
    /// developer, but a writer that does not ask the read-only sections can still change it (#701), and a header
    /// changed that way makes both wrong together rather than one of them silently.
    /// </para>
    ///
    /// <para>
    /// The view-model's own <see cref="CaretOffset"/> is moved with the text. The editor control moves its
    /// real caret itself on a document replace, but nothing pushes that back while the replace is in
    /// flight, and a document with no view attached has only this copy.
    /// </para>
    /// </remarks>
    internal void RefreshPrefix(string newPrefix)
    {
        if (string.Equals(newPrefix, bufferPrefix, StringComparison.Ordinal))
            return;

        // Clamped because a writer that does not ask the read-only sections (#701) can still delete the header,
        // leaving a document shorter than the prefix it is supposed to start with, and Replace would throw.
        var replaced = Math.Min(bufferPrefix.Length, Document.TextLength);
        var caret = CaretOffset;
        var before = bufferPrefix;

        // One undo group holding two things: the prefix change and the text change. Every write to this
        // document is recorded -- AvaloniaEdit has no unrecorded path, and NOT recording one would be worse
        // than recording it, because an undo entry holds an absolute offset and an unrecorded change above
        // it makes it replay against the wrong text.
        Document.UndoStack.StartUndoGroup();
        try
        {
            Document.UndoStack.Push(new HeaderWrite(this, before, newPrefix));
            bufferPrefix = newPrefix;
            Document.Replace(0, replaced, newPrefix);
        }
        finally
        {
            Document.UndoStack.EndUndoGroup();
        }

        if (caret >= replaced)
            CaretOffset = Math.Clamp(caret + newPrefix.Length - replaced, 0, Document.TextLength);
    }

    /// <summary>
    /// Makes the <c>Attribute VB_Name</c> line in the buffer's code say <paramref name="name"/>. Does
    /// nothing when it already does, or when the code carries no such line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For a form, a UserControl and a PropertyPage the line is in the body, not the header</b> — their
    /// code section opens with the leading <c>Attribute</c> run, which is why the prefix stops at the
    /// designer block's <c>End</c>. So this replaces one line below the prefix and leaves
    /// <see cref="bufferPrefix"/> alone. A <c>.bas</c> or <c>.cls</c> keeps its <c>VB_Name</c> in the
    /// header, where <see cref="RefreshPrefix"/> puts it; its body opens with no attribute run, so this finds
    /// nothing there and does nothing.
    /// </para>
    /// <para>
    /// <b>Recorded, and marked as the IDE's own</b>, for the reasons <see cref="RefreshPrefix"/> gives: an
    /// unrecorded change above an undo entry makes that entry replay against the wrong text, and the mark is
    /// what stops Ctrl+Z in this window undoing a rename the developer made in the Properties window.
    /// </para>
    /// </remarks>
    internal void RetargetVbName(string name)
    {
        var body = BufferBody;
        if (FormCodeText.VbNameLine(body) is not var (start, length))
            return;
        var retargeted = FormCodeText.RetargetVbName(body, name);
        if (ReferenceEquals(retargeted, body))
            return;

        var line = FormCodeText.VbNameAttribute(name);
        var offset = bufferPrefix.Length + start;
        var caret = CaretOffset;

        Document.UndoStack.StartUndoGroup();
        try
        {
            Document.UndoStack.Push(new NameWrite(this));
            Document.Replace(offset, length, line);
        }
        finally
        {
            Document.UndoStack.EndUndoGroup();
        }

        if (caret >= offset + length)
            CaretOffset = Math.Clamp(caret + line.Length - length, 0, Document.TextLength);
        else if (caret > offset)
            CaretOffset = Math.Min(caret, offset + line.Length);
    }

    /// <summary>
    /// Puts back everything the IDE owns in this buffer: the header, and the name the code section gives; and,
    /// when <paramref name="qualifiers"/> is set, the qualifiers a save wrote into members' attribute runs.
    /// </summary>
    /// <remarks>
    /// <b>The qualifiers only after an undo that undid a save's rewrite of them</b> (#273 task 4.4). Undoing one
    /// puts back the qualifiers the buffer held before that save, which a file it wrote no longer holds, so a
    /// Ctrl+Z with no code edit before it, across a save of a form changed only in its designer, would otherwise
    /// leave the window holding a description's old name under its member. They are rewritten from the text as
    /// it now stands, so after an undo that also took back a rename the rewrite finds nothing to do: the
    /// declaration and its run name the old name together. Not after every undo of the IDE's writes, which would
    /// rewrite a stale loaded qualifier that no save had rewritten, and change a document by itself.
    /// </remarks>
    private void ReassertOwnedText(bool qualifiers = false)
    {
        RefreshPrefix(ModelHeader);
        if (identity is { Name: { Length: > 0 } name })
            RetargetVbName(name);
        if (qualifiers && MemberAttributeQualifiers.Rewrites(Document.Text, bufferPrefix.Length) is { Count: > 0 } rewrites)
            WriteQualifiers(rewrites);
    }

    /// <summary>
    /// Undo in the code window: undoes the developer's last code edit, and never a designer change.
    /// </summary>
    /// <param name="popOne">
    /// Pops one undo entry. Supplied by the view as <c>TextEditor.Undo()</c> so the editor control still
    /// does its own caret and selection work; a test supplies <c>Document.UndoStack.Undo</c>.
    /// </param>
    /// <remarks>
    /// <b>A designer change reaches this window as a header write, and a header write is not an edit the
    /// developer made here.</b> The capability is explicit: a committed designer change shall not be
    /// undoable from the code window, shall not block the code window's undo of an earlier code edit, and
    /// shall not make that earlier edit undo the wrong text. Those three pull against each other, and this
    /// loop is what satisfies all of them: pop the IDE's own writes until an ordinary edit comes off with
    /// them, then put back what the IDE owns — the current header, since task 3.5 the <c>VB_Name</c> a
    /// rename wrote below it, and since task 4.4 the attribute qualifiers a save rewrote, when one was undone.
    ///
    /// <para>
    /// <b>Why the header goes back rather than the write simply being skipped.</b> An undo entry holds an
    /// absolute offset. The developer's edit was recorded against a document with the OLD header, so it can
    /// only replay correctly once that header is back — which is why the header write is undone first
    /// rather than stepped over. Re-applying afterwards leaves the window showing the form as it now is.
    /// </para>
    ///
    /// <para>
    /// <b>The cost is the redo stack</b>, which the re-application clears: a code edit undone across a
    /// designer change cannot be redone. That is a real loss, stated in the changelog rather than left to be
    /// found. It is smaller than either alternative — clearing the history destroys the undo the developer
    /// still wants, and stopping at the header write blocks undo altogether.
    /// </para>
    ///
    /// <para>
    /// The loop terminates: each turn pops an entry, and it stops at the first that is not a header write.
    /// A window holding nothing but designer changes undoes them all and puts the header back, which is the
    /// honest answer — there is no code edit in it to undo.
    /// </para>
    /// </remarks>
    internal void UndoRequested(Action popOne)
    {
        var undidOwnerWrite = false;
        qualifierWriteWasUndone = false;
        while (Document.UndoStack.CanUndo)
        {
            ownerWriteWasUndone = false;
            popOne();
            if (!ownerWriteWasUndone)
                break;
            undidOwnerWrite = true;
        }

        // Only when one was actually undone. Doing it unconditionally would push an entry and clear the
        // redo stack on every ordinary undo, which is the one thing an undo must not do. Both halves go back,
        // not only the header: a rename writes the VB_Name line in the same entry, and putting back the
        // header alone would leave the code section naming the form as it was before the rename, with
        // nothing afterwards that would ever repair it.
        if (undidOwnerWrite)
            ReassertOwnedText(qualifiers: qualifierWriteWasUndone);
    }

    public CodeEditorViewModel Initialize(FormDefinition formElement)
    {
        this.formDefinition = formElement;
        this.identity = DocumentIdentity.For(formElement);
        // Both subscriptions also reconcile the wire name. A document with no file is named
        // untitled:<Project>/<Name>, so BOTH its own rename and its project's change what servers should
        // call it, with no save involved at all -- and after #489 that is the ordinary state of a new
        // document rather than a corner case. A document that HAS a file is unaffected: its name is its
        // path, RenameAsync compares before acting, and a no-op rename costs one comparison.
        AutoDispose(formElement.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => { Title = ComputeTitle(); ReconcileWireName(); }));
        AutoDispose(formElement.Owner.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => { Title = ComputeTitle(); ReconcileWireName(); }));
        // The whole file, not the code section (#273 task 3.2). One line number now means the same
        // thing to the editor, a language server, the interpreter and the debugger.
        bufferPrefix = FormCodeText.Prefix(formElement);
        Document.Text = bufferPrefix + formElement.Code;
        ClearUndoHistoryAfterLoad();
        PlaceCaretBelowHeader();

        PopulateObjectNames();

        OpenToLanguageLayer();
        if (recentEditHistory is not null) AutoDispose(recentEditHistory.Track(identity, Document));

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
        this.identity = DocumentIdentity.For(moduleElement);
        // Both subscriptions also reconcile the wire name. A document with no file is named
        // untitled:<Project>/<Name>, so BOTH its own rename and its project's change what servers should
        // call it, with no save involved at all -- and after #489 that is the ordinary state of a new
        // document rather than a corner case. A document that HAS a file is unaffected: its name is its
        // path, RenameAsync compares before acting, and a no-op rename costs one comparison.
        AutoDispose(moduleElement.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => { Title = ComputeTitle(); ReconcileWireName(); }));
        AutoDispose(moduleElement.Owner.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => { Title = ComputeTitle(); ReconcileWireName(); }));
        // As above. For a .ctl/.pag the prefix comes from the FormPart's designer half, and the body is
        // the MODULE's code -- which is the pairing the save path writes for those kinds too.
        bufferPrefix = FormCodeText.Prefix(moduleElement);
        Document.Text = bufferPrefix + moduleElement.Code;
        ClearUndoHistoryAfterLoad();
        PlaceCaretBelowHeader();

        // A module with a designer half lists its controls like a form's editor does; one without falls
        // back to "(General)" alone, which is what this used to hardcode.
        PopulateObjectNames();

        OpenToLanguageLayer();
        if (recentEditHistory is not null) AutoDispose(recentEditHistory.Track(identity, Document));

        Title = ComputeTitle();
        return this;
    }

    /// <summary>
    /// Discards the undo entry the initial load leaves behind.
    /// </summary>
    /// <remarks>
    /// Loading the document is a <c>Document.Text</c> assignment, and AvaloniaEdit records every one — so
    /// without this the first Ctrl+Z in a freshly opened code window empties the buffer to nothing
    /// (measured, and a defect in its own right). It also matters to <see cref="UndoRequested"/>, which
    /// pops one entry past the header writes on the assumption that it is something the developer typed.
    /// </remarks>
    private void ClearUndoHistoryAfterLoad() => Document.UndoStack.ClearAll();

    /// <summary>
    /// Opens the window with its caret on the first line that can be typed on, below the header (#1518).
    /// </summary>
    /// <remarks>
    /// Offset 0 is the header's first line, which only the IDE changes, so a window opened there refused the first
    /// keystroke and showed nothing. VB6 shows no header, and its View Code puts the caret in the code. Set as the
    /// document loads, before there is a view, so a place asked for afterwards, by navigation or a breakpoint, moves it
    /// as before.
    /// </remarks>
    private void PlaceCaretBelowHeader() => CaretOffset = PastReadOnlyRegion(0);

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
        // isProjectMember: true -- this window opens forms, modules, classes, UserControls and
        // PropertyPages, and every one of them is a member. That is what gates a `.cls` away from a LaTeX
        // server claiming the same extension (#279); the carried-file editor states the opposite.
        session = new LspDocumentSession(
            lspClient, Document, DocumentWireName.For(Identity), isProjectMember: true);

        // Forwarded into this class's own event rather than re-exposed as a pass-through. The view
        // subscribes once when it attaches and never replays, so a subscription that landed on the session
        // object instead would die with it — silently, and permanently blank.
        session.MarkersChanged += markers =>
        {
            Markers = markers;
            MarkersChanged?.Invoke(markers);
        };

        // The same piggyback as before: a fresh diagnostic set means the server has evidently just re-read
        // the document, which is the cheapest signal that its symbols are worth asking for again.
        session.DiagnosticsApplied += () => _ = RefreshSymbolsAsync();

        session.Start();

        if (languageServerStateHandler is null)
        {
            // Posted rather than raised inline: this arrives on whichever thread the connection is
            // running on, and every subscriber is a view.
            languageServerStateHandler = (_, _) =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => LanguageServerStateChanged?.Invoke());
            lspClient.StateChanged += languageServerStateHandler;
            AutoDispose(new ActionDisposable(() =>
            {
                if (languageServerStateHandler is { } handler) lspClient.StateChanged -= handler;
            }));
        }
    }

    /// <summary>
    /// Replaces the whole buffer -- header and code both -- after the file watcher reloaded the underlying
    /// file from disk. Preserves the caret position best-effort. The <c>Document.Text</c> assignment raises
    /// <c>TextChanged</c>, which debounces a didChange to the LSP server so diagnostics refresh — no
    /// explicit LSP call is needed. Must be called on the UI thread.
    /// </summary>
    /// <remarks>
    /// <b>Both halves, and the prefix with them.</b> This took the code section alone and assigned it over
    /// a buffer whose prefix was still the header read when the document was opened. Since task 3.2 of
    /// hexide-io/HexIDE#273 the buffer is the whole file, so that dropped the header from the window and —
    /// because <see cref="BufferBody"/> then split at the stale prefix's length — the next flush cut the
    /// first line or two off the reloaded code and wrote the remainder back as the document. Silent data
    /// loss, and only on a file whose code section is longer than its header, which is most of them.
    ///
    /// <para>
    /// Unlike <see cref="RefreshPrefix"/> this does assign <c>Document.Text</c>, because the body changed
    /// too: there is no region to replace and no anchor below the change to preserve.
    /// </para>
    ///
    /// <para>
    /// <b>The caret is kept where it was in the code, as the marks are</b>, by moving it with the prefix's
    /// change in length. Put back at its old offset, a caret in the code landed in the header whenever the
    /// reload made the header longer -- two lines up from the first line of code, measured on a form whose
    /// designer block gained a longer caption -- and the editor unfolds a fold the caret moves into, so the
    /// header opened as though the developer had expanded it (#273 task 3.14). A caret that was in the header
    /// is left at its offset, clamped, since nothing says where the same text now is.
    /// </para>
    /// </remarks>
    internal void ReloadFrom(string newPrefix, string newBody)
    {
        var whole = newPrefix + newBody;
        var oldPrefixLength = bufferPrefix.Length;
        bufferPrefix = newPrefix;

        // The reload has already adopted the file's fidelity verdict into the same FormDefinition, so nothing
        // the framework watches changed. Raised before the early return below: an external fix to a form's
        // designer block can flip the verdict while leaving the code byte-identical. The section provider
        // reads the verdict live; this is for the banner (#475).
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsReadOnly)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ReadOnlyReason)));

        if (string.Equals(Document.Text, whole, StringComparison.Ordinal))
            return;
        var caret = CaretOffset;
        Document.Text = whole;
        if (caret >= oldPrefixLength)
            caret += newPrefix.Length - oldPrefixLength;
        CaretOffset = Math.Clamp(caret, 0, Document.TextLength);

        // The history is discarded, because what it describes is gone: every entry holds an absolute offset
        // into a document that has just been replaced wholesale from disk, and undoing back across the
        // reload would put content the file no longer has back into the buffer (#673). The designer half of
        // the same reload already does exactly this (FormEditViewModel.ReloadFromModel clears its own stack),
        // so the two halves of a reloaded document now agree rather than one of them keeping a history the
        // other threw away.
        Document.UndoStack.ClearAll();
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
            var name = ReferenceEquals(component, formDefinition.Components[0]) && RootEventPrefix is { } prefix
                ? prefix
                : component.GetPropertyOrDefault(VBProperties.NameProperty);
            if (name is { Length: > 0 })
                ObjectNames.Add(name);
        }
    }

    /// <summary>
    /// Brings the session's name back into agreement with the document, then tells servers it was saved.
    /// </summary>
    /// <remarks>
    /// Compared against <c>DocumentWireName.For(Identity)</c> rather than against anything on the event.
    /// The event matches EITHER half of a UserControl, and the code window's Save repoints the form part's
    /// path alone (#474) while the identity and the project file both use the module's — so reading the
    /// path off the event would re-open a UserControl under the wrong name, and only sometimes.
    /// </remarks>
    /// <summary>
    /// Re-announces this document under its current name, if that has changed. Does nothing otherwise.
    /// </summary>
    private void ReconcileWireName()
    {
        if (session is { } open) open.RenameAsync(DocumentWireName.For(Identity)).ListenErrors();
    }

    private async Task ReconcileThenAnnounceSaveAsync(LspDocumentSession open)
    {
        await open.RenameAsync(DocumentWireName.For(Identity));
        await open.NotifySavedAsync();
    }

    /// <summary>
    /// Cancels any pending debounce and immediately syncs the current document text to the LSP server.
    /// Call this before requests that depend on the server having up-to-date source (e.g. signatureHelp).
    /// </summary>
    internal Task FlushDocumentAsync(CancellationToken ct = default)
        => session?.FlushAsync(ct) ?? Task.CompletedTask;

    public async Task<DocumentSymbol[]> RequestSearchSymbolsAsync()
    {
        await FlushDocumentAsync();
        return LiveDocumentUri is { } uri ? await lspClient.RequestDocumentSymbolsAsync(uri) : [];
    }

    private async Task RefreshSymbolsAsync()
    {
        if (!lspClient.IsRunning) return;
        if (LiveDocumentUri is not { } uri) return;
        var symbols = await lspClient.RequestDocumentSymbolsAsync(uri);
        _symbols = symbols;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Event names come from the component, not symbols. Rebuilding that bound list reselects the
            // handler and moves a new stub's caret from its body to its declaration (#1865).
            if (SelectedObject == GeneralObject || SelectedObject is null)
                RefreshProcedureNames();
        });
    }

    private void RefreshProcedureNames()
    {
        var current = SelectedProcedure;
        ProcedureNames.Clear();

        if (SelectedObject == GeneralObject || SelectedObject is null)
        {
            ProcedureNames.Add(DeclarationsProc);
            foreach (var s in FlattenedSymbols())
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

    private string? RootEventPrefix => formDefinition?.RootVBTypeName switch
    {
        "VB.Form" => "Form",
        "VB.UserControl" => "UserControl",
        _ => null,
    };

    private ComponentInstance? FindComponent(string name)
    {
        if (formDefinition is null) return null;
        if (name == RootEventPrefix)
            return formDefinition.Components[0];
        foreach (var c in formDefinition.Components)
            if (c.GetPropertyOrDefault(VBProperties.NameProperty) == name)
                return c;
        return null;
    }

    private void OnSelectedObjectChanged(string? oldValue, string? newValue)
    {
        RefreshProcedureNames();
    }

    /// <summary>
    /// Every symbol the server reported, nesting included.
    /// </summary>
    /// <remarks>
    /// <c>textDocument/documentSymbol</c> answers with a tree, and servers differ on how deep they make it.
    /// The bundled VB6 server returns procedures at the top level; a server that reports the module as one
    /// symbol containing its procedures is equally conformant, and reading only the top level of that
    /// answer yields a procedure dropdown with the module's name in it and nothing else.
    /// </remarks>
    private IEnumerable<DocumentSymbol> FlattenedSymbols() =>
        _symbols is null ? [] : _symbols.SelectMany(s => s.Flatten());

    /// <summary>
    /// The name this document's LIVE session is known by, or null when no session is open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read off the session, not minted per request.</b> It used to call
    /// <c>DocumentWireName.For(Identity)</c> on every request, which reads the path live — so from the
    /// moment a document was first saved, every lifecycle notification still said <c>untitled:</c> while
    /// every request said <c>file:</c>. The bundled server answers an unknown URI with an empty array and
    /// no error, so hover, completion, folding, Go To Definition, rename and formatting simply went quiet,
    /// and the procedure dropdown emptied on the next diagnostics tick. Nothing logged, nothing thrown.
    /// </para>
    /// <para>
    /// Null when nothing is open, and the callers below answer emptily rather than asking. A request naming
    /// a document no server was told about is not a question with a wrong answer; it is a question nobody
    /// can be expected to have an answer to, and sending it invites exactly the silence above.
    /// </para>
    /// <para>
    /// Make EXE is why this must not be recomputed even when a session IS open: it repoints every
    /// document's path into a temporary folder and puts it back afterwards, so a name derived from the path
    /// mid-build would name a document the server never opened, twice.
    /// </para>
    /// </remarks>
    private string? LiveDocumentUri => session is { IsOpen: true } open ? open.Uri : null;

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

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ApplyFormatting(edits));
        }
        catch (Exception ex)
        {
            Log.Debug("[save-format] {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// Applies a server's formatting answer: the lines it changes, less any in a read-only region, as one
    /// undo step. Format on Save and Format Document both come through here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never applied as sent</b> (#273 phase 3, the formatting row of the writer policy). The bundled
    /// server used to answer with one edit replacing the whole document and re-indenting every designer line
    /// to column zero. Applied as sent, that rewrote the header. The body is split off by the header's LENGTH,
    /// so a shorter header also cut into the code: measured on a VB6-authored form, Format on Save lost the
    /// whole attribute block and sixteen lines below it. Since task 3.10 the bundled server sends one edit per
    /// run of changed lines and leaves protected lines out, but another server need not. So the answer is
    /// applied to a copy, reduced to the lines it actually changes, and anything touching the header or a
    /// member's attribute run is dropped. A kept whole-line deletion of a complete logical declaration then
    /// takes its run with it (#1890).
    /// </para>
    /// <para>
    /// Lines are compared without their terminators, so a server answering in <c>\n</c> for a
    /// <c>\r\n</c> buffer does not rewrite every line. The lines that are written keep the buffer's own
    /// terminator.
    /// </para>
    /// </remarks>
    /// <returns>What was applied and what was left out, for the callers that want to say so.</returns>
    internal Reduction ApplyFormatting(IReadOnlyList<TextEdit> edits)
    {
        var before = Document.Text;
        var reduction = LineEdits.ReduceFormatting(before, Rewrite(before, edits),
            ReadOnlyRegions.Of(before, bufferPrefix.Length), MemberRunsNow);

        if (reduction.Changes.Count > 0)
        {
            // One undo step, applied from the end so each change's offset still holds.
            var lastFirst = new List<TextChange>(reduction.Changes);
            lastFirst.Reverse();
            ApplyAsOneStep(lastFirst);
        }

        if (reduction.DroppedLines > 0)
            Log.Information("Formatting left {Lines} line(s) of {Document} alone: they are in its header or a member's attribute lines",
                reduction.DroppedLines, identity?.Display);
        return reduction;
    }

    /// <summary>
    /// <paramref name="text"/> — which is this buffer's text — as <paramref name="edits"/> would leave it.
    /// </summary>
    /// <remarks>
    /// Positions are resolved against the live document's own line map, which is the one the server was
    /// sent. A position past the last line, which a whole-document edit often uses for its end, means the end
    /// of the text rather than an error.
    /// </remarks>
    private string Rewrite(string text, IReadOnlyList<TextEdit> edits)
    {
        var builder = new StringBuilder(text);
        foreach (var edit in Resolve(edits))
            builder.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Text);
        return builder.ToString();
    }

    /// <summary>
    /// <paramref name="edits"/> as offsets into the live buffer, last first, so each can be applied without
    /// moving the ones still to come.
    /// </summary>
    private List<TextChange> Resolve(IReadOnlyList<TextEdit> edits)
    {
        int OffsetOf(Position position)
        {
            if (position.Line >= Document.LineCount)
                return Document.TextLength;
            var line = Document.GetLineByNumber(position.Line + 1);
            return Math.Min(line.Offset + position.Character, line.EndOffset);
        }

        var resolved = new List<TextChange>(edits.Count);
        foreach (var edit in edits)
        {
            var start = OffsetOf(edit.Range.Start);
            resolved.Add(new TextChange(start, Math.Max(OffsetOf(edit.Range.End) - start, 0), edit.NewText));
        }
        resolved.Sort((a, b) => b.Offset.CompareTo(a.Offset));
        return resolved;
    }

    /// <summary>Applies changes that are already last first, as one undo step.</summary>
    private void ApplyAsOneStep(IReadOnlyList<TextChange> lastFirst)
    {
        Document.BeginUpdate();
        try
        {
            foreach (var change in lastFirst)
                Document.Replace(change.Offset, change.Length, change.Text);
        }
        finally
        {
            Document.EndUpdate();
        }
    }

    // ── The guarded write path (#273 task 3.9) ────────────────────────────────────────────────────────
    //
    // Every programmatic write into this buffer that is not the IDE's own goes through one of the members
    // below. The read-only section provider task 3.7 installs covers TYPING only; formatting, rename,
    // Replace, completion, Insert File, Enter, add-ins and automation all write the document directly and
    // never meet it. The policy for each is the design record's writer table.

    /// <summary>The read-only regions of this buffer as it stands: its header, and each member's attribute lines.</summary>
    /// <remarks>
    /// Cached against the document's version and the prefix it was composed with. The section provider asks
    /// on every keystroke, and the scan reads the whole buffer.
    /// </remarks>
    internal IReadOnlyList<TextRegion> ReadOnlyRegionsNow
    {
        get
        {
            var version = Document.Version;
            if (regionsCache is null || !ReferenceEquals(regionsCacheVersion, version)
                || regionsCachePrefixLength != bufferPrefix.Length)
            {
                regionsCache = ReadOnlyRegions.Of(Document.Text, bufferPrefix.Length);
                regionsCacheVersion = version;
                regionsCachePrefixLength = bufferPrefix.Length;
            }
            return regionsCache;
        }
    }

    private IReadOnlyList<TextRegion>? regionsCache;
    private ITextSourceVersion? regionsCacheVersion;
    private int regionsCachePrefixLength;

    /// <summary>
    /// Every run of attribute lines below this buffer's header as it stands, those describing a member and the
    /// orphans alike (<see cref="ReadOnlyRegions.MemberRuns"/>).
    /// </summary>
    /// <remarks>
    /// Cached as <see cref="ReadOnlyRegionsNow"/> is, the prefix's length included: a reload whose text is the
    /// buffer's own but whose split is not returns before it changes the document, so the version alone would keep
    /// runs read against the old split. The view reads it once an edit group has changed the text and once as the
    /// group begins, to keep a fold on each run that describes a member (#273 task 4.3), and when a step to the right
    /// lands at the end of one; never at every change of a group, which made a format of N lines N whole-buffer
    /// scans (task 4.3 review, <see cref="MemberRunScans"/>).
    /// </remarks>
    internal IReadOnlyList<MemberRun> MemberRunsNow
    {
        get
        {
            var version = Document.Version;
            if (runsCache is null || !ReferenceEquals(runsCacheVersion, version)
                || runsCachePrefixLength != bufferPrefix.Length)
            {
                runsCache = ReadOnlyRegions.MemberRuns(Document.Text, bufferPrefix.Length);
                runsCacheVersion = version;
                runsCachePrefixLength = bufferPrefix.Length;
                MemberRunScans++;
            }
            return runsCache;
        }
    }

    /// <summary>
    /// How many times <see cref="MemberRunsNow"/> has read the whole buffer, for the tests that hold the code window
    /// to one read per edit group however many changes the group makes (#273 task 4.3 review).
    /// </summary>
    internal int MemberRunScans { get; private set; }

    private IReadOnlyList<MemberRun>? runsCache;
    private ITextSourceVersion? runsCacheVersion;
    private int runsCachePrefixLength;

    /// <summary>
    /// The section provider the view installs on its text area (task 3.7): the regions above plus the
    /// whole-document verdict, both read live, so it never needs replacing.
    /// </summary>
    internal CodeWindowReadOnlySections ReadOnlySections =>
        readOnlySections ??= new CodeWindowReadOnlySections(Document, () => IsReadOnly, () => ReadOnlyRegionsNow,
            () => MemberRunsNow);

    private CodeWindowReadOnlySections? readOnlySections;

    /// <inheritdoc/>
    public bool IsReadOnlyRegion(int offset, int length) =>
        ReadOnlyRegions.Changes(ReadOnlyRegionsNow, offset, length);

    /// <summary>
    /// Whether a keystroke's write at this span is refused: the whole file is held read-only
    /// (<see cref="IsReadOnly"/>), or the span changes a read-only region. This is the question the section
    /// provider answers for AvaloniaEdit's own typing, asked by the window's keys that write the document
    /// themselves (Enter's auto-close and auto-indent, a completion's commit), so the banner's "typing in this
    /// window is refused" holds for them too.
    /// </summary>
    /// <remarks>
    /// The programmatic writers (Replace All, formatting, rename, Insert File, add-ins, automation's whole
    /// replacement) still ask <see cref="IsReadOnlyRegion"/> alone; giving them the whole-file verdict is #701.
    /// </remarks>
    public bool RefusesTyping(int offset, int length) =>
        IsReadOnly || IsReadOnlyRegion(offset, length);

    /// <inheritdoc/>
    public (int Offset, int Length) WidenDeletion(int offset, int length) =>
        ReadOnlySections.WidenDeletion(offset, length);

    /// <inheritdoc/>
    public Func<int, int, bool> SnapshotReadOnlyRegions()
    {
        var regions = ReadOnlyRegionsNow;
        return (offset, length) => ReadOnlyRegions.Changes(regions, offset, length);
    }

    /// <summary>
    /// Where text meant for <paramref name="offset"/> goes instead, for a writer whose text "goes after the
    /// region": the end of the read-only region the offset falls in, or the offset itself when it falls in
    /// none.
    /// </summary>
    internal int PastReadOnlyRegion(int offset)
    {
        foreach (var region in ReadOnlyRegionsNow)
        {
            if (region.Touches(offset, 0))
                return region.End;
        }
        return offset;
    }

    /// <summary>
    /// Edit &gt; Insert File: asks for a file and puts its text in place of the selection, or after a read-only
    /// region the selection touches.
    /// </summary>
    /// <remarks>
    /// The picker is the window manager's, which is the one automation can answer
    /// (<c>answer_next_file_dialog</c>). The view used to ask the storage provider itself — the only picker in
    /// the IDE that did — so an automated run opened a real native dialog and waited for a person.
    /// </remarks>
    internal async Task InsertFileAsync(int selectionStart, int selectionLength)
    {
        var files = await windowManager.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localization.GetString("Str.CodeEditor.Dialog.InsertFileTitle"),
            AllowMultiple = false,
        });
        if (files is not { Count: > 0 })
            return;

        var content = DocumentNewLines.Normalize(await System.IO.File.ReadAllTextAsync(files[0]), Document.Text);
        if (InsertPastReadOnlyRegion(selectionStart, selectionLength, content) is { } caret)
        {
            CaretOffset = caret;
            return;
        }
        // Keep the described declaration before its run and place the remaining file text after it (task 4.5).
        if (InsertLineBreakPastRun(selectionStart, selectionLength, content) is { } placed)
        {
            CaretOffset = placed.End;
            return;
        }
        Document.Replace(selectionStart, selectionLength, content);
        CaretOffset = selectionStart + content.Length;
    }

    /// <summary>
    /// Insert File's text, when the selection it would replace touches a read-only region: written after the
    /// region instead, on lines of its own, replacing nothing.
    /// </summary>
    /// <returns>
    /// Where the caret goes after the inserted text; or null when the selection touches no region, and the
    /// caller replaces it as usual — the selection is the developer's choice everywhere else.
    /// </returns>
    internal int? InsertPastReadOnlyRegion(int selectionStart, int selectionLength, string content)
    {
        if (!IsReadOnlyRegion(selectionStart, selectionLength))
            return null;

        var at = PastReadOnlyRegion(selectionStart);
        if (at == selectionStart)
            at = PastReadOnlyRegion(selectionStart + selectionLength);

        var newline = DocumentNewLines.Of(Document.Text);
        content = TextUtilities.NormalizeNewLines(content, newline);
        if (!content.EndsWith('\n') && !content.EndsWith('\r'))
            content += newline;
        // A region that runs to the end of a file with no final line break ends mid-line.
        if (at > 0 && Document.GetCharAt(at - 1) is not ('\n' or '\r'))
            content = newline + content;

        Document.Insert(at, content);
        return at + content.Length;
    }

    /// <summary>
    /// Writes text holding a line break that would replace <c>[offset, offset + length)</c> at the end of a line a
    /// member's run describes, keeping its declaration before the run and the remaining text after it, as one undo step
    /// (#273 task 4.5). The section provider decides where (<see cref="CodeWindowReadOnlySections.PlaceLineBreak"/>).
    /// </summary>
    /// <returns>Where the two parts went; or null when the rule does not apply, and nothing was written.</returns>
    /// <remarks>
    /// The window's line breaks all come here: Enter and Shift+Enter, a paste, Insert File and automation's
    /// <c>type_text</c>. Put where the text asked, the line break would leave the run hanging from the last line of
    /// the text, describing whatever that declares and most often nothing.
    /// </remarks>
    internal LineBreakPastRun? InsertLineBreakPastRun(int offset, int length, string text)
    {
        if (ReadOnlySections.PlaceLineBreak(offset, length, text) is not { } placed)
            return null;

        Document.BeginUpdate();
        try
        {
            // The later offset first, so the earlier one still means what it meant.
            Document.Insert(placed.TailAt, placed.Tail);
            if (placed.Length > 0 || placed.Head.Length > 0)
                Document.Replace(placed.Offset, placed.Length, placed.Head);
        }
        finally
        {
            Document.EndUpdate();
        }
        return placed;
    }

    /// <summary>Refuses answers reaching other documents before applying any of this document's edits.</summary>
    internal string? ApplyRename(WorkspaceEdit edit, string oldName)
    {
        if (edit.Changes is null) return null;
        var uri = GetDocumentUriPublic();
        var others = edit.Changes
            .Where(c => c.Value.Length > 0 && !LspDocumentUri.AreSame(c.Key, uri))
            .Select(c => editorService.DocumentNameForUri(c.Key) ?? c.Key)
            .ToArray();
        if (others.Length > 0)
            return string.Format(localization.GetString("Str.CodeEditor.Msg.RenameReachesOtherDocuments"),
                string.Join(", ", others));

        var edits = edit.Changes.FirstOrDefault(c => LspDocumentUri.AreSame(c.Key, uri)).Value;
        return edits is null ? null : ApplyRename(edits, oldName);
    }

    /// <summary>
    /// Applies a server's rename, or refuses it as a whole when it would change a read-only region.
    /// </summary>
    /// <param name="edits">The server's edits to this document.</param>
    /// <param name="oldName">The name being renamed, which a member's own attribute lines are allowed to follow.</param>
    /// <returns>Null when applied; otherwise the reason, in words for the developer.</returns>
    /// <remarks>
    /// <para>
    /// <b>Refused rather than clipped</b>, unlike formatting. A rename is one change to one name: applied in
    /// part, it leaves the name meaning two things. And the refusal is not rare, because the bundled server's
    /// rename is lexical and whole-word over the buffer — with a designer block in it, renaming a local
    /// called <c>Caption</c>, <c>Top</c> or <c>Index</c>, or a control's name, would otherwise rewrite the
    /// form's layout. A control is renamed in the Properties window, as in VB6.
    /// </para>
    /// <para>
    /// <b>The one write into a region it allows</b> is a member's qualifier in its own attribute lines, when the
    /// same answer renames that member (<see cref="RefusedRenameEdit"/>): <c>Attribute Total.VB_Description</c>
    /// follows <c>Total</c> becoming <c>GrandTotal</c>. Nothing depends on a server sending it, since the next
    /// save of the document rewrites those qualifiers anyway (#273 task 4.4), but a server that does is right to.
    /// </para>
    /// </remarks>
    internal string? ApplyRename(IReadOnlyList<TextEdit> edits, string oldName)
    {
        var changes = Resolve(edits);

        if (RefusedRenameEdit(Document.Text, bufferPrefix.Length, changes) is { } refused)
        {
            Log.Information("Refused a rename of {Name} in {Document}: an edit at offset {Offset} is in a read-only region",
                oldName, identity?.Display, refused.Offset);
            return localization.GetString("Str.CodeEditor.Msg.RenameTouchesReadOnly");
        }

        ApplyAsOneStep(changes);
        return null;
    }

    /// <summary>
    /// The first of <paramref name="changes"/>, a rename's edits to <paramref name="text"/>, that would change a
    /// read-only region other than by a qualifier the rename may write; null when the rename may make them all.
    /// </summary>
    /// <param name="text">The whole buffer.</param>
    /// <param name="prefixLength">The length of the header it was composed with; see <see cref="ReadOnlyRegions.Of"/>.</param>
    /// <param name="changes">Every edit of the answer to this document.</param>
    /// <remarks>
    /// <para>
    /// <b>A qualifier edit is let through when it replaces exactly one attribute line's qualifier, in a run that
    /// describes a member, and the same answer renames that member</b>: it replaces the name on the declaration
    /// above the run with the same new text. For a procedure's run the member is the procedure, whatever the
    /// qualifier said, because VB6 gives the run to the procedure by position (design.md, "Renames (4.4)"). For a
    /// variable run it is the declarator the line's qualifier names, since VB6 binds that line by name; and for a
    /// run a rename by retyping left, it is the one name its line declares.
    /// </para>
    /// <para>
    /// <b>Variables are let through too, which the design paragraph does not say.</b> It names a run describing a
    /// procedure, and "every other edit to a run" refused. Read that way, renaming a described variable from the
    /// bundled server, which sends <c>Attribute Count.VB_VarDescription</c>'s qualifier edit with the
    /// declaration's, would be refused as a whole where it was applied before, though the run still names a name
    /// its line declares afterwards and so still describes it.
    /// </para>
    /// <para>
    /// <b>"Renames the member" is read off the answer, not off a name.</b> The rule this replaced let a qualifier
    /// through whenever it spelled the old name, so renaming <c>Total</c> also rewrote a stale
    /// <c>Attribute Total.</c> line under <c>Halt</c>, a run that is <c>Halt</c>'s. That edit is now refused, and
    /// with it the rename, which is the policy for every edit to a run that the rename does not own. The bundled
    /// server no longer sends it.
    /// </para>
    /// <para>
    /// Internal and static so that <c>BundledServerRespectsTheIdesRegionsTests</c> judges the bundled server's
    /// answers by this rule rather than by a copy of it.
    /// </para>
    /// </remarks>
    internal static TextChange? RefusedRenameEdit(string text, int prefixLength, IReadOnlyList<TextChange> changes)
    {
        var regions = ReadOnlyRegions.Of(text, prefixLength);
        IReadOnlyList<MemberRun>? runs = null;

        foreach (var change in changes)
        {
            if (!ReadOnlyRegions.Changes(regions, change.Offset, change.Length))
                continue;
            runs ??= ReadOnlyRegions.MemberRuns(text, prefixLength);
            if (!IsQualifierTheRenameOwns(runs, changes, change))
                return change;
        }
        return null;
    }

    /// <summary>Whether <paramref name="change"/> is a qualifier of a member that <paramref name="changes"/> renames.</summary>
    private static bool IsQualifierTheRenameOwns(IReadOnlyList<MemberRun> runs, IReadOnlyList<TextChange> changes, TextChange change)
    {
        foreach (var run in runs)
        {
            // DescribesMember is defensive. Only an edit that changes a region is asked about, and an orphan is no
            // region and overlaps no run, so an edit starting in one reaches a region only past its own line, and is
            // then none of its qualifiers: the answer is false either way.
            if (!run.DescribesMember || change.Offset < run.Region.Start || change.Offset >= run.Region.End)
                continue;
            foreach (var line in run.Lines)
            {
                if (line.Qualifier is not { } qualifier || line.QualifierOffset != change.Offset
                    || qualifier.Length != change.Length)
                    continue;

                DeclaredName? member = run.Member;
                if (run.Kind == MemberRunKind.Variable)
                {
                    member = null;
                    foreach (var declared in run.Declared)
                    {
                        if (declared.Kind != DeclarationKind.Procedure
                            && string.Equals(declared.Name, qualifier, StringComparison.OrdinalIgnoreCase))
                            member = declared;
                    }
                }
                if (member is not { } renamed)
                    return false;
                foreach (var other in changes)
                {
                    if (other.Offset == renamed.Offset && other.Length == renamed.Name.Length
                        && string.Equals(other.Text, change.Text, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
            return false;
        }
        return false;
    }

    /// <summary>
    /// Why <paramref name="newName"/> cannot be the new name of a rename, or null when it may be: refused before
    /// the request is sent (#273 task 4.4), so a server is never asked to write it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rule names are chosen by everywhere else</b> (<see cref="ProjectNaming.IsValidName"/>): a letter, then
    /// letters, digits and underscores. Each refusal it makes is measured or certain. A leading or lone
    /// underscore and a bracketed name were measured illegal against <c>vb6.exe</c> (the oracle's
    /// <c>continuation-vs-identifier</c> and "VB6 has no bracket-escaped identifiers"), and a leading digit, a
    /// space, a <c>.</c> or a type-declaration character cannot be part of a name at all.
    /// </para>
    /// <para>
    /// <b>Then the six words VB6 was measured to refuse though they have a name's shape</b>
    /// (<see cref="ProjectNaming.IsReservedWord"/>): <c>Rem</c>, <c>Empty</c>, <c>New</c>, <c>Nothing</c>,
    /// <c>Me</c> and <c>Attribute</c>. Each has a message of its own, because the shape rule's says what a name
    /// is made of, and these are made of exactly that. Renamed to <c>Rem</c>, <c>Function Total</c> would no
    /// longer compile, and a body line <c>Rem = mSum</c> would turn into a comment, in VB6 and in the interpreter
    /// alike.
    /// </para>
    /// <para>
    /// <b>What it does not refuse, because nothing measured says to</b>: any other keyword, a name past any
    /// length, and a letter outside ASCII, which VB6 may accept in its code page.
    /// </para>
    /// </remarks>
    internal string? NewNameRefusal(string newName) =>
        !ProjectNaming.IsValidName(newName)
            ? string.Format(localization.GetString("Str.Naming.Msg.NotAVb6Name"), newName)
            : ProjectNaming.IsReservedWord(newName)
                ? string.Format(localization.GetString("Str.Naming.Msg.ReservedWord"), newName)
                : null;

    /// <summary>
    /// Why <paramref name="word"/> cannot be renamed from <paramref name="offset"/>, or null when it can.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A name in the header or in a member's attribute lines is not the developer's to rename there: a control
    /// is renamed in the Properties window, and a member from its declaration, which its attribute lines then
    /// follow.
    /// </para>
    /// <para>
    /// Nor is a name the designer block declares, wherever the caret is. Its declaration is in the header, so
    /// a rename that keeps off the header renames the code's references and not the control, and the code no
    /// longer compiles. Before a server kept off the header this was refused anyway, because its answer
    /// reached the header; now it has to be refused on purpose.
    /// </para>
    /// <para>
    /// Asked before the name prompt rather than left to the server's answer, because a server keeping to the
    /// language-server rule answers both with nothing at all (hexide-io/HexIDE#273 task 3.10), and nothing is
    /// indistinguishable from a rename that found no occurrences.
    /// </para>
    /// </remarks>
    internal string? RenameRefusalAt(int offset, string word) =>
        IsReadOnlyRegion(offset, 0) || ReadOnlyRegions.DeclaredNames(Document.Text, bufferPrefix.Length).Contains(word)
            ? localization.GetString("Str.CodeEditor.Msg.RenameTouchesReadOnly")
            : null;

    /// <summary>
    /// Replaces this document's content, keeping its header: the rule add-in <c>SetContent</c> and automation
    /// <c>set_file_content</c> share. Refused when the content would change the header, or a member's attribute
    /// lines other than by moving or renaming them with it or deleting them with its declaration (#273 task 4.5b).
    /// </summary>
    public ContentReplacement ReplaceContent(string incoming)
    {
        var result = GuardedContent.Replace(Document.Text, bufferPrefix.Length, DocumentNewLines.Normalize(incoming, Document.Text));
        if (result.NewText is { } text)
            ReplaceBody(text[bufferPrefix.Length..]);
        return result;
    }

    /// <summary>
    /// Applies edits an add-in computed against this buffer, or refuses them all when any would change a
    /// read-only region.
    /// </summary>
    /// <param name="edits">The edits, as offsets in this buffer.</param>
    /// <param name="describe">
    /// How the refusal names the edit at an index, in the caller's own terms. Without it the edit is named by the
    /// 1-based line and column its offset falls at, counted from the top of the file.
    /// </param>
    /// <returns>Null when applied; otherwise why not, naming the refused edit or the members the result would change.</returns>
    internal string? ApplyEdits(IReadOnlyList<TextChange> edits, Func<int, string>? describe = null)
    {
        var regions = ReadOnlyRegionsNow;
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (ReadOnlyRegions.Changes(regions, edit.Offset, edit.Length))
            {
                var at = Document.GetLocation(edit.Offset);
                return $"{describe?.Invoke(i) ?? $"The edit at line {at.Line}, column {at.Column}"} would change "
                       + WhatAnEditChanges(regions, edit) + ", which only the IDE changes. Nothing was changed.";
            }
        }

        // An edit putting a line break at the end of a described line would leave the member's run hanging from
        // the edit's last line, which mostly declares nothing (#273 task 4.5). The window moves such a line break
        // past the run; an add-in's range is the add-in's own choice of place, so it is refused instead.
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (edit.Text.AsSpan().IndexOfAny('\r', '\n') >= 0
                && ReadOnlySections.RunHangingFrom(edit.Offset + edit.Length) is { Member: { } member })
            {
                var at = Document.GetLocation(edit.Offset);
                return $"{describe?.Invoke(i) ?? $"The edit at line {at.Line}, column {at.Column}"} would put a line "
                       + $"break at the end of the declaration of {member.Name}, between it and its Attribute lines, "
                       + "which follow the line they describe. Put new lines at the start of the line after the "
                       + "Attribute lines instead. Nothing was changed.";
            }
        }

        var lastFirst = new List<TextChange>();
        foreach (var edit in edits)
        {
            var span = edit.Text.Length == 0 ? WidenDeletion(edit.Offset, edit.Length) : (edit.Offset, edit.Length);
            lastFirst.Add(new TextChange(span.Item1, span.Item2, edit.Text));
        }
        lastFirst.Sort((a, b) => b.Offset.CompareTo(a.Offset));
        for (var i = 1; i < lastFirst.Count; i++)
        {
            var edit = lastFirst[i];
            if (edit.Offset + edit.Length > lastFirst[i - 1].Offset
                && ReadOnlyRegions.Changes(regions, edit.Offset, edit.Length))
            {
                var at = Document.GetLocation(edit.Offset);
                return $"The edit at line {at.Line}, column {at.Column} would change "
                       + WhatAnEditChanges(regions, edit) + ", which only the IDE changes. Nothing was changed.";
            }
        }
        var after = Document.Text;
        foreach (var edit in lastFirst)
            after = after.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Text);
        if (GuardedContent.RangeEditRefusal(Document.Text, after, bufferPrefix.Length) is { } refusal)
            return string.Format(localization.GetString("Str.CodeEditor.RangeEditRefused"), refusal);

        ApplyAsOneStep(lastFirst);
        return null;
    }

    /// <summary>
    /// What a refused edit would change, as its refusal says it: the header, or the attribute lines of each member
    /// whose run it reaches, named as a refused replacement names them (#273 task 4.5b).
    /// </summary>
    /// <remarks>
    /// The header is named when the edit reaches it at all, as it always was; an edit reaching only members' runs
    /// names those members, where it used to say "the header or a member's Attribute lines" and leave the caller to
    /// work out which.
    /// </remarks>
    private string WhatAnEditChanges(IReadOnlyList<TextRegion> regions, TextChange edit)
    {
        var members = new List<string>();
        foreach (var region in regions)
        {
            if (!region.Changes(edit.Offset, edit.Length))
                continue;
            if (MemberNamedBy(region) is not { } member)
                return "the document's header (its designer block, class header or leading Attribute lines)";
            members.Add(member);
        }
        return "the Attribute lines describing " + (members.Count == 1
            ? members[0]
            : string.Join(", ", members.GetRange(0, members.Count - 1)) + " and " + members[^1]);
    }

    /// <summary>The shared member name used by guarded edits and automation refusals.</summary>
    internal string? MemberNamedBy(TextRegion region) =>
        GuardedContent.MemberNamedBy(Document.Text, bufferPrefix.Length, region);

    /// <summary>Shows a refusal from one of the guarded writers to the developer.</summary>
    internal Task ShowRefusalAsync(string message) =>
        windowManager.MessageBox(message, icon: MessageBoxIcon.Information);

    public Task<HoverResult?> RequestHoverAsync(Position position, CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestHoverAsync(uri, position, ct) : Task.FromResult<HoverResult?>(null);

    public Task<SemanticTokenResult?> RequestSemanticTokensAsync(CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestSemanticTokensAsync(uri, ct) : Task.FromResult<SemanticTokenResult?>(null);

    public Task<FoldingRange[]> RequestFoldingRangesAsync(CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestFoldingRangesAsync(uri, ct) : Task.FromResult<FoldingRange[]>([]);

    public IReadOnlyList<string> CompletionTriggerCharacters => LiveDocumentUri is { } uri
        ? lspClient.CompletionTriggerCharacters(uri) : [];

    public Task<CompletionItem[]> RequestCompletionForTriggerAsync(
        Position position, string triggerCharacter, CancellationToken ct = default) =>
        LiveDocumentUri is { } uri
            ? lspClient.RequestCompletionForTriggerAsync(uri, position, triggerCharacter, ct)
            : Task.FromResult<CompletionItem[]>([]);

    public Task<CompletionItem[]> RequestCompletionAsync(Position position, CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestCompletionAsync(uri, position, ct) : Task.FromResult<CompletionItem[]>([]);

    public Task<SignatureHelp?> RequestSignatureHelpAsync(Position position, CancellationToken ct = default, string? triggerCharacter = null)
        => LiveDocumentUri is { } uri ? lspClient.RequestSignatureHelpAsync(uri, position, ct, triggerCharacter) : Task.FromResult<SignatureHelp?>(null);

    public Task<Location[]?> RequestDefinitionAsync(Position position, CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestDefinitionAsync(uri, position, ct) : Task.FromResult<Location[]?>(null);

    public Task<Location[]?> RequestDeclarationAsync(Position position, CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestDeclarationAsync(uri, position, ct) : Task.FromResult<Location[]?>(null);

    public Task<DocumentHighlight[]?> RequestDocumentHighlightAsync(Position position, CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestDocumentHighlightAsync(uri, position, ct) : Task.FromResult<DocumentHighlight[]?>(null);

    public Task<RenameResult?> RequestRenameAsync(Position position, string newName, CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestRenameAsync(uri, position, newName, ct) : Task.FromResult<RenameResult?>(null);

    public Task<string?> ShowRenameInputBoxAsync(string defaultText)
        => windowManager.InputBox(localization.GetString("Str.CodeEditor.Dialog.RenamePrompt"),
            localization.GetString("Str.CodeEditor.Dialog.RenameTitle"), defaultText);

    public Task<TextEdit[]> RequestFormattingAsync(CancellationToken ct = default)
        => LiveDocumentUri is { } uri ? lspClient.RequestFormattingAsync(uri, ct) : Task.FromResult<TextEdit[]>([]);

    /// <summary>
    /// Exposes the document URI for cross-file definition navigation and for the rename edit lookup.
    /// </summary>
    /// <remarks>
    /// Falls back to what a session WOULD be opened under when none is open, because both callers are
    /// comparing a server's answer against "this document" and a null would read as "not this one" —
    /// which, for a reply that can only have been about this document, is the wrong answer rather than a
    /// cautious one. The requests themselves are gated; a comparison is not a request.
    /// </remarks>
    public string GetDocumentUriPublic() => LiveDocumentUri ?? DocumentWireName.For(Identity);

    /// <summary>
    /// Opens the document a server's <c>Location</c> names and puts the caret on it.
    /// </summary>
    /// <remarks>
    /// <b>This used to be a no-op, justified by a comment about the BUNDLED server.</b> It said the server
    /// "currently only returns symbols from the same file" — true of ours, and a statement about one
    /// backend rather than about the protocol. A foreign server that answers properly stops it being true
    /// without anything here changing, and the failure is silent: the definition is found, returned, and
    /// dropped.
    /// </remarks>
    public void NavigateToUri(string uri, int line, int col)
    {
        // Line and column arrive one-based from the caller, which has already converted from the
        // protocol's zero-based Position.
        if (!editorService.NavigateTo(uri, line, col))
            Log.Debug("[definition] Nothing loaded answers to {Uri}", uri);
    }

    private void OnSelectedProcedureChanged(string? oldValue, string? newValue)
    {
        if (newValue is null or DeclarationsProc) return;

        if (SelectedObject == GeneralObject || SelectedObject is null)
        {
            // Navigate to existing proc by symbol range
            // Searched over the same flattened view the dropdown was filled from. Looking only at the top
            // level here would offer a name and then decline to navigate to it.
            var sym = FlattenedSymbols().FirstOrDefault(s => s.Name == newValue);
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
            var headerEnd = ReadOnlyRegions.HeaderEnd(Document.Text, bufferPrefix.Length);
            var idx = Document.IndexOf(searchText, headerEnd, Document.TextLength - headerEnd, StringComparison.OrdinalIgnoreCase);
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
        sb.Append(")\n");

        // The shape a double-click gives, with the caret on the blank line inside (#1532).
        InsertAtEnd(sb.ToString(), "\nEnd Sub\n");
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

    /// <summary>
    /// Adds a procedure at the end of the code, as VB6 adds one, and puts the caret between <paramref name="beginCode"/>
    /// and <paramref name="endCode"/>.
    /// </summary>
    /// <remarks>
    /// Measured in VB6's IDE (#1532): a procedure goes straight after the header when there is no code below it, and
    /// otherwise one blank line below the code. Its line breaks are the file's, as a paste's are: the generators write
    /// <c>\n</c>. This used to put <c>\n\n</c> in front of whatever the file ended with, so a form's CRLF file took two
    /// bare line feeds and a procedure in LF.
    /// </remarks>
    private void InsertAtEnd(string beginCode, string endCode)
    {
        var lineBreak = DocumentNewLines.Of(Document.Text);
        beginCode = TextUtilities.NormalizeNewLines(beginCode, lineBreak);
        endCode = TextUtilities.NormalizeNewLines(endCode, lineBreak);

        var text = Document.Text;
        var separator = text.Length == 0 || text.EndsWith('\n') || text.EndsWith('\r') ? "" : lineBreak;
        var hasCode = PastReadOnlyRegion(0) < text.Length;
        if (hasCode && !(text + separator).EndsWith(lineBreak + lineBreak, StringComparison.Ordinal))
            separator += lineBreak;
        Document.Insert(Document.TextLength, separator);

        Document.Insert(Document.TextLength, beginCode);
        var offset = Document.TextLength;
        Document.Insert(Document.TextLength, endCode);
        CaretOffset = offset;
        FocusWindowRequest?.Invoke();
    }
}
