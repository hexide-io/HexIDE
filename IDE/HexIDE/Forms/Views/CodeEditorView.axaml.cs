using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Labs.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using HexIDE.Bookmarks;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Editor;
using HexIDE.Runtime.Serialization;
using HexIDE.Themes;
using HexIDE.Utils;
using R3;
using Serilog;

namespace HexIDE.Forms.Views;

public partial class CodeEditorView : UserControl
{
    private IDisposable? sub;
    private LspTextMarkerService? _markerService;
    private LspDiagnosticsColorizer? _colorizer;
    private BracketDepthColorizer? _bracketDepthColorizer;
    private ReadOnlyRegionColorizer? _readOnlyColorizer;
    private ReadOnlyRegionLinks? _readOnlyLinks;
    private ReadOnlyRegionRedraw? _readOnlyRedraw;
    private IDisposable? _readOnlyTextSub;
    private CancellationTokenSource? _hoverCts;
    private BookmarkMargin? _bookmarkMargin;
    private Point _lastHoverPoint;
    private FoldingManager? _foldingManager;
    private StickyScrollController? _stickyScroll;
    private CancellationTokenSource? _foldCts;
    private SemanticTokensColorizer? _semanticColorizer;
    private int _semanticVersion;
    private CompletionWindow? _completionWindow;
    private CancellationTokenSource? _completionCts;
    private OverloadInsightWindow? _insightWindow;
    private CancellationTokenSource? _signatureCts;
    // The last completion and parameter-info requests, as far as showing what came back.
    private Task _completionShown = Task.CompletedTask;
    private Task _signatureShown = Task.CompletedTask;

    /// <summary>
    /// What the completion list and parameter info <paramref name="editor"/> last asked the language server for are
    /// still waiting on, null when nothing is: type_text's keyboard mode waits for it after each key, as a person's next
    /// key comes after the list a letter opened has appeared (#943 review). A request a later key superseded ends then.
    /// </summary>
    internal static Task? PendingPopupsOf(TextEditor editor) =>
        Avalonia.LogicalTree.LogicalExtensions.FindLogicalAncestorOfType<CodeEditorView>(editor) is { } view
        && ReferenceEquals(view.TextEditor, editor)
        && !(view._completionShown.IsCompleted && view._signatureShown.IsCompleted)
            ? Task.WhenAll(view._completionShown, view._signatureShown)
            : null;

    /// <summary>
    /// The completion list and parameter info open over <paramref name="editor"/>, the list first, for the automation
    /// surface (#942). Both are popups parented only logically, to the window, so neither the visual walk nor the routes
    /// to a control's menus and flyouts reaches them, and a list that was open read as none. Both can be open at once,
    /// as typing an argument opens a list under the call's parameter info, and then both are returned (#942 review).
    /// </summary>
    internal static IReadOnlyList<Avalonia.Controls.Primitives.Popup> OpenPopupsOf(TextEditor editor) =>
        Avalonia.LogicalTree.LogicalExtensions.FindLogicalAncestorOfType<CodeEditorView>(editor) is { } view && ReferenceEquals(view.TextEditor, editor)
            ? new Avalonia.Controls.Primitives.Popup?[] { view._completionWindow, view._insightWindow }
                .Where(p => p is { IsOpen: true }).Select(p => p!).ToList()
            : [];
    private DocumentHighlightRenderer? _highlightRenderer;
    private CancellationTokenSource? _highlightCts;
    private HexIDE.Debugging.BreakpointMargin? _breakpointMargin;
    private HexIDE.Debugging.CurrentLineRenderer? _currentLineRenderer;
    private IndentGuideRenderer? _indentGuideRenderer;
    private HexIDE.Tools.CallStackToolViewModel? _callStack;
    private Action? _onFrameShown;
    private HexIDE.Runtime.Debugging.IDebugController? _debugController;
    private HexIDE.Debugging.ExternalDebugService? _externalDebugger;
    private Action? _onExternalDebugChanged;
    private Action<HexIDE.Runtime.Debugging.StoppedInfo>? _onDebugStopped;
    private Action? _onDebugContinued;
    private System.ComponentModel.PropertyChangedEventHandler? _vmSelectionSync;
    private bool _resetPromptOpen;
    private string? _preEditBody;       // the code as it stood before the first edit while running (for Cancel→revert)
    private bool _resetPromptArmed;     // developer input is being handled; the next change it makes asks to reset
    private TextDocument? _watchedDocument;
    private int _preEditCaret;
    private Action? _onPaletteChanged;

    public DelegateCommand Undo { get; }
    public DelegateCommand Redo { get; }
    public DelegateCommand Copy { get; }
    public DelegateCommand Cut { get; }
    public DelegateCommand Delete { get; }
    public DelegateCommand Paste { get; }
    public DelegateCommand PasteAsComment { get; }
    public DelegateCommand Find { get; }
    public DelegateCommand Replace { get; }
    public DelegateCommand SelectAll { get; }

    public CodeEditorView()
    {
        // Through the view model, because a designer change reaches this window as a header write and a
        // header write is not an edit the developer made here (#273 task 3.8). TextEditor.Undo is still
        // what pops each entry -- it is AvaloniaEdit's own command route and does the caret and selection
        // work that Document.UndoStack.Undo alone does not -- so the view supplies the mechanism and the
        // view model decides how many times to use it.
        Undo = new DelegateCommand(
            () =>
            {
                if (DataContext is CodeEditorViewModel vm)
                    vm.UndoRequested(() => TextEditor.Undo());
                else
                    TextEditor.Undo();
            },
            () => TextEditor?.CanUndo ?? false);
        Redo = new DelegateCommand(() => TextEditor.Redo(), () =>
            TextEditor?.CanRedo ?? false);
        Copy = new DelegateCommand(() => TextEditor.Copy(), () => true);
        // Cut, Delete and Paste are where their keys arrive, not OnEditorKeyDown: each key is bound to its command,
        // which takes it before the text area sees it. So each command asks about resetting a run itself, for the
        // key, the Edit menu, the context menu and the toolbar alike (#273 task 3.13).
        // A cut or a delete of a described line takes its attribute run with it, onto the clipboard too, so each
        // widens the selection first (#273 task 4.5a); a cut with nothing selected takes the caret's line.
        Cut = new DelegateCommand(() =>
        {
            WidenSelectionForDeletion(wholeLineWhenEmpty: TextEditor.Options.CutCopyWholeLine);
            AskBeforeClipboardEdit(paste: false);
            TextEditor.Cut();
        }, () => true);
        Delete = new DelegateCommand(() =>
        {
            ArmResetPrompt();
            WidenSelectionForDeletion(wholeLineWhenEmpty: false);
            if (TextEditor.TextArea.Selection.IsEmpty)
                EditingCommands.Delete.Execute(null, TextEditor.TextArea);
            else
                TextEditor.Delete();
        }, () => true);
        // A paste at the end of a described line is the window's own, so a line break in it can go past the run
        // (task 4.5); the library's paste puts the text wherever the caret is, and only the text says whether it may.
        // A paste over a box is the window's own too, written as typing over it is, because the library's hangs (#1251).
        Paste = new DelegateCommand(() =>
        {
            AskBeforeClipboardEdit(paste: true);
            if (TextEditor.TextArea.Selection is RectangleSelection { IsEmpty: false })
                _ = PasteOverBoxAsync();
            else if (SelectionEndsWhereARunHangs())
                _ = PasteAtTheEndOfADescribedLineAsync();
            else
                TextEditor.Paste();
        }, () => true);
        PasteAsComment = new DelegateCommand(() => _ = PasteAsCommentAsync(), () => true);
        SelectAll = new DelegateCommand(() => TextEditor.SelectAll(), () => true);
        Find = new DelegateCommand(() =>
        {
            // Handled by MainView command binding — this is a fallback
        }, () => true);
        Replace = new DelegateCommand(() =>
        {
            // Handled by MainView command binding — this is a fallback
        }, () => true);

        InitializeComponent();
        InstallReadOnlySections();

        // One Find surface, the region-aware dialog (#273 task 3.11). AvaloniaEdit installs its own search
        // panel on every editor when the template is applied, and its Replace All writes straight to the
        // document, header included. MainView claims Ctrl+F and Ctrl+H first while a searchable document is
        // active, but not when that command cannot run or when MainView is not in the route, so the panel
        // is removed rather than left to a gesture race. The event follows OnApplyTemplate, where it is made.
        TextEditor.TemplateApplied += (_, _) => TextEditor.SearchPanel?.Uninstall();

        TextEditor.TextChanged += TextChanged;
        TextEditor.TextChanged += OnTextChangedForFolding;

        TextEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
    }

    private void TextChanged(object? sender, EventArgs e)
    {
        CommandManager.InvalidateRequerySuggested();
    }

    public void Indent()
    {
        ArmResetPrompt();
        EditingCommands.TabForward.Execute(null, TextEditor.TextArea);
    }

    public void Outdent()
    {
        ArmResetPrompt();
        EditingCommands.TabBackward.Execute(null, TextEditor.TextArea);
    }

    public void TriggerCompletion() => TriggerCompletionAsync();

    public void TriggerSignatureHelp() => TriggerSignatureHelpAsync();

    public async Task InsertFile()
    {
        if (DataContext is CodeEditorViewModel vm)
            await vm.InsertFileAsync(TextEditor.SelectionStart, TextEditor.SelectionLength);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        InstallReadOnlySections();
    }

    /// <summary>
    /// Both read-only gates in one provider (#273 task 3.7), installed with the view model rather than on
    /// attach, so a view is never typable before it is shown. Never bind <c>TextEditor.IsReadOnly</c>:
    /// setting it replaces this provider outright, and the region rules go with it.
    /// </summary>
    private void InstallReadOnlySections()
    {
        // DataContext can arrive by inheritance before InitializeComponent; the constructor installs it then.
        if (TextEditor is not null && DataContext is CodeEditorViewModel vm)
            TextEditor.TextArea.ReadOnlySectionProvider = vm.ReadOnlySections;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        sub?.Dispose();
        sub = null;
        if (DataContext is CodeEditorViewModel vm)
        {
            sub = vm.ObservePropertyChanged(x => x.CaretOffset)
                .Subscribe(offset =>
                {
                    var caret = Math.Clamp(offset, 0, TextEditor.Document.TextLength);
                    if (TextEditor.CaretOffset != caret)
                    {
                        // A caret navigated into a fold is opened onto, or it would sit unseen, or be moved off a
                        // folded member run's end, where it was put (#700 review).
                        if (_foldingManager is { } folds)
                            foreach (var fold in folds.GetFoldingsContaining(caret))
                                if (fold.IsFolded && fold.StartOffset < caret && caret <= fold.EndOffset)
                                    fold.IsFolded = false;
                        TextEditor.CaretOffset = caret;
                        // Navigation can arrive while this editor is already active. Moving the
                        // caret alone does not reveal an offscreen caller or next statement.
                        TextEditor.ScrollToLine(TextEditor.TextArea.Caret.Line);
                    }
                    // A clamp may leave the caret where it already was, so no PositionChanged event writes it back.
                    if (vm.CaretOffset != TextEditor.CaretOffset)
                        vm.CaretOffset = TextEditor.CaretOffset;
                });
            // A view made for a caret already placed, as navigating to a document not yet open places it, scrolls to
            // it once it has been laid out: before then there is nothing to scroll, and ScrollToLine does nothing.
            if (vm.CaretOffset > 0)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (DataContext == vm)
                        TextEditor.ScrollToLine(TextEditor.TextArea.Caret.Line);
                }, Avalonia.Threading.DispatcherPriority.Loaded);

            // Sync selection from ViewModel → TextEditor. Stored in a field so it can be removed on detach — a
            // Dock document-move re-materialises the view against the SAME persistent VM, so an unremoved handler
            // would accumulate on the VM and fire redundantly on every later selection change.
            _vmSelectionSync = (_, args) =>
            {
                if (args.PropertyName is nameof(CodeEditorViewModel.SelectionStart) or nameof(CodeEditorViewModel.SelectionLength))
                {
                    if (TextEditor.SelectionStart != vm.SelectionStart ||
                        TextEditor.SelectionLength != vm.SelectionLength)
                    {
                        TextEditor.Select(vm.SelectionStart, vm.SelectionLength);
                        if (vm.SelectionLength > 0)
                            TextEditor.TextArea.Caret.BringCaretToView();
                    }
                }
            };
            vm.PropertyChanged += _vmSelectionSync;

            vm.FocusWindowRequest += VmOnFocusWindowRequest;

            // Edit-and-Continue's prompt opens on the document's own Changing event, armed by input (see
            // ArmResetPrompt), and the header fold's state is read there before a change can remove the fold.
            // Kept so detach removes both from the same document: the view model, and with it the document,
            // outlives a dock move that re-materialises this view.
            _watchedDocument = vm.Document;
            _watchedDocument.Changing += OnDocumentChangingForResetPrompt;
            _watchedDocument.UpdateStarted += OnDocumentUpdateStartedForFolding;
            _watchedDocument.Changing += OnDocumentChangingForFolding;

            // Attach LSP text marker service (squiggles) and diagnostics colorizer (red text)
            _markerService = new LspTextMarkerService(TextEditor);
            TextEditor.TextArea.TextView.BackgroundRenderers.Add(_markerService);

            _bracketDepthColorizer = new BracketDepthColorizer(TextEditor, this, vm.Settings);
            TextEditor.TextArea.TextView.LineTransformers.Add(_bracketDepthColorizer);

            // The header and each member's Attribute lines in the theme's read-only colour (#273 task 3.15).
            // Added before the diagnostics colorizer, so an error inside one still shows red; AvaloniaEdit
            // keeps its own syntax colorizer ahead of both, so the grey replaces the syntax colours there.
            _readOnlyColorizer = new ReadOnlyRegionColorizer(() => vm.ReadOnlyRegionsNow);
            TextEditor.TextArea.TextView.LineTransformers.Add(_readOnlyColorizer);
            _readOnlyTextSub = ReadOnlyText.Follow(this, OnReadOnlyTextChanged);
            // An address inside a region would be drawn as a link, over the grey; ahead of the link generators,
            // this claims it as plain text. The folding generator goes ahead of it when installed below.
            _readOnlyLinks = new ReadOnlyRegionLinks(() => vm.ReadOnlyRegionsNow, TextEditor.TextArea.TextView);
            TextEditor.TextArea.TextView.ElementGenerators.Insert(0, _readOnlyLinks);
            // Both read the regions as each line is built, and AvaloniaEdit rebuilds only the lines an edit touches,
            // while a member's run turns into an orphan or out of one by an edit to the line above it (task 4.1).
            _readOnlyRedraw = new ReadOnlyRegionRedraw(vm.Document, TextEditor.TextArea.TextView, () => vm.ReadOnlyRegionsNow);

            InstallConditionalCompilation();

            _colorizer = new LspDiagnosticsColorizer(TextEditor.TextArea.TextView);
            TextEditor.TextArea.TextView.LineTransformers.Add(_colorizer);
            vm.MarkersChanged += OnMarkersChanged;
            vm.LanguageServerStateChanged += OnLanguageServerStateChanged;

            // Caught up rather than waiting for the next publication. A dock move detaches this view and
            // re-materialises it around fresh renderers holding nothing, and neither the language server
            // nor the VB6 toolchain has any reason to publish again for a document that has not changed —
            // so without this the squiggles vanish on a dock move and stay gone until the next edit or
            // rebuild. (#270)
            OnMarkersChanged(vm.Markers);

            _indentGuideRenderer = new IndentGuideRenderer(TextEditor, vm.Settings);
            TextEditor.TextArea.TextView.BackgroundRenderers.Add(_indentGuideRenderer);

            // Document highlights (background renderer, behind squiggles)
            _highlightRenderer = new DocumentHighlightRenderer(TextEditor);
            TextEditor.TextArea.TextView.BackgroundRenderers.Insert(0, _highlightRenderer);
            TextEditor.TextArea.Caret.PositionChanged += OnCaretMovedForHighlight;

            // Code folding. The header's fold and each member's attribute run's are the window's own and are in
            // place before anything is drawn; a server's folds join them when the server answers, if one ever does.
            _foldingManager = FoldingManager.Install(TextEditor.TextArea);
            TextEditor.TextArea.Caret.PositionChanged += KeepCaretOffFoldedRunsEnd;
            _stickyScroll = new StickyScrollController(StickyHeaders, TextEditor, vm.Settings,
                () => _foldingManager?.AllFoldings.Where(f =>
                    !ReferenceEquals(f.Tag, HeaderFoldTag) && !ReferenceEquals(f.Tag, RunFoldTag)) ?? []);
            ApplyFolds(vm, []);
            _semanticColorizer = new SemanticTokensColorizer();
            TextEditor.TextArea.TextView.LineTransformers.Insert(
                TextEditor.TextArea.TextView.LineTransformers.IndexOf(_readOnlyColorizer), _semanticColorizer);
            _ = RequestSemanticTokensAsync(vm, CancellationToken.None);
            _ = RequestFoldingsAsync(vm, CancellationToken.None);

            // Bookmark gutter margin
            _bookmarkMargin = new BookmarkMargin(vm.BookmarkService, vm.Identity, vm.ToggleBookmark);
            TextEditor.TextArea.LeftMargins.Insert(0, _bookmarkMargin);

            // Debugger: breakpoint gutter (red dots, click-to-toggle) + the amber current-statement bar.
            _breakpointMargin = new HexIDE.Debugging.BreakpointMargin(vm.BreakpointService, vm.Identity, vm.ToggleBreakpoint,
                () => vm.ExternalDebugger?.IsActive == true,
                () => vm.ExternalDebugger?.GetBreakpoints(vm.GetDocumentUriPublic()) ?? [], vm.DescribeExternalBreakpoint);
            TextEditor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
            _currentLineRenderer = new HexIDE.Debugging.CurrentLineRenderer(TextEditor);
            TextEditor.TextArea.TextView.BackgroundRenderers.Insert(0, _currentLineRenderer);

            _debugController = vm.DebugController;
            _externalDebugger = vm.ExternalDebugger;
            if (_externalDebugger is not null)
            {
                int? displayedExecutionLine = null;
                _onExternalDebugChanged = () =>
                {
                    _hoverCts?.Cancel();
                    _hoverCts?.Dispose();
                    _hoverCts = null;
                    CloseTip();
                    _breakpointMargin?.RefreshExternal();
                    int? line = null;
                    if (_externalDebugger.CurrentLocation is { } location)
                    {
                        if (string.Equals(location.Uri, vm.GetDocumentUriPublic(),
                                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                            line = location.Line;
                    }
                    _currentLineRenderer?.SetLine(line);
                    if (line != displayedExecutionLine && line is > 0 && line <= TextEditor.Document.LineCount)
                        TextEditor.ScrollToLine(line.Value);
                    displayedExecutionLine = line;
                };
                _externalDebugger.Changed += _onExternalDebugChanged;
                _onExternalDebugChanged();
            }
            _onDebugStopped = info => ShowCurrentStatement(vm, info.Module, info.Line);
            _onDebugContinued = () => _currentLineRenderer?.SetLine(null);
            _debugController.Stopped += _onDebugStopped;
            _debugController.Continued += _onDebugContinued;
            // If the interpreter is already paused (this tab was opened by the Stopped bridge after the break),
            // show the current statement immediately rather than waiting for the next event.
            if (_debugController.CurrentStop is { } stop)
                ShowCurrentStatement(vm, stop.Module, stop.Line);

            _callStack = vm.CallStack;
            if (_callStack is not null)
            {
                _onFrameShown = () =>
                {
                    var frame = _callStack.ShownFrame;
                    var here = frame?.Module is { } module && vm.RunningProject is { } running
                        && vm.Identity.IsIn(running) && vm.Identity.IsNamed(module);
                    _breakpointMargin?.SetCallSite(here && frame!.IsCaller ? frame.Line : null);
                    if (here && frame!.Line > 0 && frame.Line <= TextEditor.Document.LineCount)
                    {
                        vm.CaretOffset = TextEditor.Document.GetLineByNumber(frame.Line).Offset;
                        TextEditor.ScrollToLine(frame.Line);
                    }
                };
                _callStack.FrameShown += _onFrameShown;
                _onFrameShown();
            }

            // Hover tooltip
            ToolTip.SetPlacement(TextEditor, PlacementMode.Pointer);
            ToolTip.SetShowDelay(TextEditor, 0);
            TextEditor.PointerMoved += OnPointerMoved;
            TextEditor.PointerExited += OnPointerExited;

            // IntelliSense completion
            TextEditor.TextArea.TextEntered  += OnTextEntered;
            TextEditor.TextArea.TextEntering += OnTextEntering;
            // Use Tunnel routing so we intercept Enter BEFORE AvaloniaEdit's
            // TextArea.OnKeyDown inserts a default newline.
            TextEditor.TextArea.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            // And once the text area has handled a key, or something has taken it first: on the editor, an ancestor
            // of the text area, so it runs after the library's own handling whatever order its handlers were added
            // in (#273 task 4.3 review).
            TextEditor.AddHandler(KeyDownEvent, OnEditorKeyHandled, Avalonia.Interactivity.RoutingStrategies.Bubble,
                handledEventsToo: true);

            TextEditor.SyntaxHighlighting = SyntaxHighlightingTheme.Definition;

            // Minimap (prototype)
            Minimap.Attach(TextEditor, SyntaxHighlightingTheme.Definition);

            // On a theme switch SyntaxHighlightingTheme has already retinted the shared definition by
            // the time this fires — but AvaloniaEdit baked the old brushes into the visual lines it
            // built, so Redraw() is what clears that cache and re-runs the colorizer. This is what
            // makes ALREADY-OPEN editors recolour live instead of only on restart.
            _onPaletteChanged = () => TextEditor.TextArea.TextView.Redraw();
            SyntaxHighlightingTheme.PaletteChanged += _onPaletteChanged;

            // Apply tab width from settings
            TextEditor.Options.IndentationSize = vm.Settings.TabWidth;
            TextEditor.Options.ConvertTabsToSpaces = true;
            TextEditor.Options.AllowToggleOverstrikeMode = true;

            // Push initial cursor position to status bar
            var caret = TextEditor.TextArea.Caret;
            vm.StatusBar.Line = caret.Line;
            vm.StatusBar.Column = caret.Column;
            vm.StatusBar.InsertMode = !TextEditor.TextArea.OverstrikeMode;
        }
    }

    // Paint (and reveal) the amber current-statement bar when the interpreter breaks in THIS document; clear it
    // when the break is elsewhere. Lines are 1-based (matching the runtime and the breakpoint store). Invoked on
    // the UI thread — the controller raises Stopped from the interpreter's own (UI-thread) execution.
    /// <summary>
    /// Shows or clears the amber current-statement bar for a pause the interpreter has just reported.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The controller names a module and nothing else, which is right on its own terms — a run is one
    /// project and its gate knows nothing of the IDE's documents. So the decision is made here, against the
    /// document's identity: it has to be <em>in the running project</em> as well as answer to that name.
    /// A group whose two projects each hold a <c>Module1</c> would otherwise light the bar in both editors,
    /// one of which is not executing anything.
    /// </para>
    /// <para>
    /// The name is read off the definition at this moment rather than captured when the view attached. A
    /// captured one stops matching the moment the document is renamed, and the bar then never appears
    /// again in that editor.
    /// </para>
    /// </remarks>
    private void ShowCurrentStatement(CodeEditorViewModel vm, string module, int line)
    {
        if (_currentLineRenderer is null)
            return;

        var executingHere = vm.RunningProject is { } running
                            && vm.Identity.IsIn(running)
                            && vm.Identity.IsNamed(module);
        if (!executingHere)
        {
            _currentLineRenderer.SetLine(null);
            return;
        }
        _currentLineRenderer.SetLine(line);
        if (line >= 1 && line <= TextEditor.Document.LineCount)
        {
            var docLine = TextEditor.Document.GetLineByNumber(line);
            TextEditor.CaretOffset = docLine.Offset;
            TextEditor.ScrollToLine(line);
        }
    }

    private void OnMarkersChanged(IReadOnlyList<LspMarker> markers)
    {
        _markerService?.SetMarkers(markers);
        _colorizer?.SetMarkers(markers);
    }

    private void VmOnFocusWindowRequest()
    {
        TextEditor.Focus();
        TextEditor.ScrollToLine(TextEditor.TextArea.Caret.Line);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Minimap.Detach();
        _indentGuideRenderer?.Dispose();
        _indentGuideRenderer = null;
        _stickyScroll?.Dispose();
        _stickyScroll = null;

        // PaletteChanged is a static event: a Dock document-move re-materialises this view, so an
        // unremoved handler leaks it for the process lifetime and fires a stale redraw per theme
        // switch. Same hazard _vmSelectionSync below is guarded against.
        if (_onPaletteChanged is not null)
        {
            SyntaxHighlightingTheme.PaletteChanged -= _onPaletteChanged;
            _onPaletteChanged = null;
        }

        if (DataContext is CodeEditorViewModel vm)
        {
            vm.FocusWindowRequest -= VmOnFocusWindowRequest;
            vm.MarkersChanged -= OnMarkersChanged;
            if (_watchedDocument is not null)
            {
                _watchedDocument.Changing -= OnDocumentChangingForResetPrompt;
                _watchedDocument.UpdateStarted -= OnDocumentUpdateStartedForFolding;
                _watchedDocument.Changing -= OnDocumentChangingForFolding;
                _watchedDocument = null;
            }
            vm.LanguageServerStateChanged -= OnLanguageServerStateChanged;
            if (_vmSelectionSync is not null)
            {
                vm.PropertyChanged -= _vmSelectionSync;
                _vmSelectionSync = null;
            }

            // Clear status bar cursor info when this editor detaches
            vm.StatusBar.Line = null;
            vm.StatusBar.Column = null;
        }

        if (_markerService is not null)
        {
            TextEditor.TextArea.TextView.BackgroundRenderers.Remove(_markerService);
            _markerService = null;
        }

        if (_colorizer is not null)
        {
            TextEditor.TextArea.TextView.LineTransformers.Remove(_colorizer);
            _colorizer = null;
        }

        RemoveConditionalCompilation();

        _readOnlyTextSub?.Dispose();
        _readOnlyTextSub = null;
        if (_bracketDepthColorizer is not null)
        {
            TextEditor.TextArea.TextView.LineTransformers.Remove(_bracketDepthColorizer);
            _bracketDepthColorizer.Dispose();
            _bracketDepthColorizer = null;
        }

        if (_readOnlyColorizer is not null)
        {
            TextEditor.TextArea.TextView.LineTransformers.Remove(_readOnlyColorizer);
            _readOnlyColorizer = null;
        }
        if (_readOnlyLinks is not null)
        {
            TextEditor.TextArea.TextView.ElementGenerators.Remove(_readOnlyLinks);
            _readOnlyLinks = null;
        }
        _readOnlyRedraw?.Dispose();
        _readOnlyRedraw = null;

        if (_foldingManager is not null)
        {
            // The view model outlives this view across a dock move, and the folds come back the way they are now.
            if (DataContext is CodeEditorViewModel foldOwner)
                ObserveOwnFolds(foldOwner);
            TextEditor.TextArea.Caret.PositionChanged -= KeepCaretOffFoldedRunsEnd;
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
            _runKeys.Clear();
        }

        if (_bookmarkMargin is not null)
        {
            TextEditor.TextArea.LeftMargins.Remove(_bookmarkMargin);
            _bookmarkMargin = null;
        }

        if (_breakpointMargin is not null)
        {
            TextEditor.TextArea.LeftMargins.Remove(_breakpointMargin);
            _breakpointMargin = null;
        }

        if (_currentLineRenderer is not null)
        {
            TextEditor.TextArea.TextView.BackgroundRenderers.Remove(_currentLineRenderer);
            _currentLineRenderer = null;
        }

        if (_callStack is not null && _onFrameShown is not null) _callStack.FrameShown -= _onFrameShown;
        _callStack = null;
        _onFrameShown = null;

        if (_debugController is not null)
        {
            if (_externalDebugger is not null && _onExternalDebugChanged is not null)
                _externalDebugger.Changed -= _onExternalDebugChanged;
            _externalDebugger = null;
            _onExternalDebugChanged = null;
            if (_onDebugStopped is not null) _debugController.Stopped -= _onDebugStopped;
            if (_onDebugContinued is not null) _debugController.Continued -= _onDebugContinued;
            _debugController = null;
            _onDebugStopped = null;
            _onDebugContinued = null;
        }

        _foldCts?.Cancel();
        _foldCts?.Dispose();
        _foldCts = null;
        _semanticVersion++;
        if (_semanticColorizer is not null)
            TextEditor.TextArea.TextView.LineTransformers.Remove(_semanticColorizer);
        _semanticColorizer = null;

        TextEditor.PointerMoved -= OnPointerMoved;
        TextEditor.PointerExited -= OnPointerExited;
        TextEditor.TextArea.TextEntered  -= OnTextEntered;
        TextEditor.TextArea.TextEntering -= OnTextEntering;
        TextEditor.TextArea.KeyDown      -= OnEditorKeyDown;
        TextEditor.RemoveHandler(KeyDownEvent, OnEditorKeyHandled);
        _stepRightFrom = -1;
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = null;
        CloseTip();

        _completionCts?.Cancel();
        _completionCts?.Dispose();
        _completionCts = null;

        _signatureCts?.Cancel();
        _signatureCts?.Dispose();
        _signatureCts = null;

        if (_highlightRenderer is not null)
        {
            TextEditor.TextArea.TextView.BackgroundRenderers.Remove(_highlightRenderer);
            _highlightRenderer = null;
        }
        TextEditor.TextArea.Caret.PositionChanged -= OnCaretMovedForHighlight;
        _highlightCts?.Cancel();
        _highlightCts?.Dispose();
        _highlightCts = null;

        sub?.Dispose();
        sub = null;
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (DataContext is CodeEditorViewModel vm)
        {
            vm.CaretOffset = TextEditor.CaretOffset;

            var caret = TextEditor.TextArea.Caret;
            vm.StatusBar.Line = caret.Line;
            vm.StatusBar.Column = caret.Column;
            vm.StatusBar.InsertMode = !TextEditor.TextArea.OverstrikeMode;
        }
    }

    private void OnTextChangedForFolding(object? sender, EventArgs e)
    {
        // Binding the document before attach is covered by the initial folding request on attach.
        if (_foldingManager is null || DataContext is not CodeEditorViewModel vm) return;
        // Not left to the debounced request below: a reload removes every fold, and the header would show
        // expanded for half a second, or for good were the request to fail. A member's run is the same, and one
        // an edit turns into an orphan has to lose its fold now rather than when a server next answers (task 4.3).
        if (!OwnFoldsAreCurrent(vm))
            ApplyFolds(vm, null);
        _semanticVersion++;
        if (_semanticColorizer is { } semantic)
        {
            semantic.Tokens = [];
            TextEditor.TextArea.TextView.Redraw();
        }
        ScheduleFolding(vm);
    }

    /// <summary>
    /// Greys the read-only regions, and labels folds, in the theme's colour as it stands: on attach and on every
    /// change of theme, including one between two dark packs that no variant change announces.
    /// </summary>
    private void OnReadOnlyTextChanged(IBrush? brush)
    {
        if (_readOnlyColorizer is null) return;
        _readOnlyColorizer.Brush = brush;
        TextEditor.TextArea.TextView.Redraw();
    }

    /// <summary>Marks the folding section that is the header's, among the ones a server's answer creates.</summary>
    private static readonly object HeaderFoldTag = new();

    /// <summary>Marks a folding section as a member's attribute run's (#273 task 4.3).</summary>
    /// <remarks>
    /// It carries no key. Which run a section is the fold of is read from where it is, against the runs as the text
    /// stands, because an edit to the declaration a run describes can change its key without moving its fold: a name
    /// retyped on the line, or a property's other accessor gaining a run above it. A key kept on the tag would only
    /// ever be read where the offsets no longer match, just after a change or part-way through an edit group, and by
    /// then the state has been recorded at an earlier change, under the key the run had then.
    /// </remarks>
    private static readonly object RunFoldTag = new();

    /// <summary>
    /// Applies the window's own folds and a server's together: the header's fold, folded unless the developer
    /// expanded it in this window (#273 task 3.14), and the fold of each member's attribute run, folded unless the
    /// developer expanded that run (task 4.3).
    /// </summary>
    /// <param name="server">The server's folds, or null to keep the ones shown.</param>
    /// <remarks>
    /// <para>
    /// <b>The window sets the fold's state itself, every time.</b> The folding manager cannot be left to keep
    /// it. It honours <see cref="NewFolding.DefaultClosed"/> only in its first update. It unfolds every section
    /// it removes, and it removes them all on a reload and the header's on a rewrite covering the whole fold.
    /// And a server fold listed first that starts where the header does took the header's section, leaving the
    /// header's fold new and expanded. Measured headlessly on AvaloniaEdit 12.0.0, 2026-10-02; that it pairs
    /// folds with sections by start offset alone is read from its source, and agrees.
    /// <see cref="Vb6FoldingAdapter.Merge"/> drops such a fold.
    /// </para>
    /// <para>
    /// <b>Whether the developer expanded it is read from the fold before every re-creation</b>, because the
    /// folding manager raises nothing when a fold is toggled. So it is read here, before the document
    /// changes and before the view detaches. An expansion the editor makes itself, when the caret moves into
    /// the folded header, is read as the developer's too, which is why nothing the IDE does on its own may put
    /// the caret there.
    /// </para>
    /// <para>
    /// <b>A run's section the manager paired keeps its live state</b>; only one it made new takes the state
    /// remembered for the run's key. A section still paired with a run is
    /// the very fold the developer is looking at, whose key an edit to the declaration may just have changed. A new
    /// section is also made expanded when the caret is inside it or at its end, as it is when a developer types a
    /// run in under its declaration: folded, the line being typed would vanish under the caret.
    /// </para>
    /// <para>
    /// <b>None of the window's own folds is carried forward as a server's.</b> When no server answer is given, the
    /// server's folds already shown are kept, which are the sections not tagged as the window's. A run that has
    /// stopped describing its member keeps its tag until this application drops it, so its fold goes with it.
    /// </para>
    /// </remarks>
    private void ApplyFolds(CodeEditorViewModel vm, IReadOnlyList<NewFolding>? server)
    {
        if (_foldingManager is not { } manager) return;
        ObserveOwnFolds(vm);

        var shown = new List<NewFolding>();
        var runSections = new Dictionary<FoldingSection, bool>();
        foreach (var section in manager.AllFoldings)
        {
            if (ReferenceEquals(section.Tag, RunFoldTag))
                runSections[section] = section.IsFolded;
            else if (server is null && !ReferenceEquals(section.Tag, HeaderFoldTag))
                shown.Add(new NewFolding(section.StartOffset, section.EndOffset) { Name = section.Title });
        }

        var previousHeader = HeaderSection();
        var header = Vb6FoldingAdapter.Header(vm.Document, vm.ReadOnlyRegionsNow);
        var freshHeader = previousHeader is null || header is null
                          || previousHeader.StartOffset != header.StartOffset || previousHeader.EndOffset != header.EndOffset;
        var runs = vm.MemberRunsNow;
        var own = Vb6FoldingAdapter.MemberRuns(runs);
        if (header is not null)
            own.Insert(0, header);
        Vb6FoldingAdapter.Update(manager, Vb6FoldingAdapter.Merge(own, server ?? shown));
        foreach (var section in manager.AllFoldings)
        {
            if (ReferenceEquals(section.Tag, RunFoldTag) || ReferenceEquals(section.Tag, HeaderFoldTag))
                section.Tag = null;
        }

        // The header's section is the one at its offsets. The manager kept the old one if it still starts at the
        // top of the file; other folds can share its start, so the end must match too. A new
        // one is left open over a caret inside it, as navigating to a header line in a document not yet open puts it
        // there before the folds exist, which would fold it away under the caret (#700 review).
        var caret = TextEditor.TextArea.Caret.Offset;
        if (header is not null)
        {
            foreach (var section in manager.GetFoldingsAt(header.StartOffset))
            {
                if (section.EndOffset != header.EndOffset) continue;
                section.Tag = HeaderFoldTag;
                section.IsFolded = !vm.HeaderFoldExpanded
                                   && !(freshHeader && section.StartOffset < caret && caret <= section.EndOffset);
            }
        }

        foreach (var run in runs)
        {
            if (run.Key is not { } key || RunSection(run) is not { } section) continue;
            if (runSections.TryGetValue(section, out var folded))
                section.IsFolded = folded;
            else
            {
                var caretInside = section.StartOffset < caret && caret <= section.EndOffset;
                section.IsFolded = !caretInside && !vm.ExpandedMemberRuns.Contains(key);
            }
            section.Tag = RunFoldTag;
        }
    }

    /// <summary>
    /// Records on the view model whether the header's fold and each run's are expanded, while they are still shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run's state is recorded under the key of the run whose fold the section is where it now stands. One that
    /// matches no run is passed over. That happens only after a change that moved the run and not yet its fold, as
    /// the window re-makes its folds after it, and the group's first <c>Changing</c> recorded the state while the
    /// section still matched. Nothing the developer does can change it in between.
    /// </para>
    /// <para>
    /// <b>A key a section has moved off is dropped</b> (task 4.3 review): the run it is the fold of was renamed,
    /// or renumbered by a run of the same member added or removed above it, so the key is its old one, and kept it
    /// would open whatever run takes that key next. Retyping <c>Go</c> as <c>Gap</c> key by key left <c>GO</c>,
    /// <c>G</c> and <c>GA</c> expanded, and a property's Let gaining a run above its Get's opened as the Get's did.
    /// Every key left is dropped before any is recorded, so two runs that trade keys in one group each keep their
    /// own state. A key no section moved off, a deleted run's, is kept for Undo.
    /// </para>
    /// </remarks>
    private void ObserveOwnFolds(CodeEditorViewModel vm)
    {
        if (HeaderSection() is { } header)
            vm.HeaderFoldExpanded = !header.IsFolded;
        if (_foldingManager is null) return;

        var runs = vm.MemberRunsNow;
        var matched = new List<(FoldingSection Section, MemberRunKey Key)>();
        var keys = new Dictionary<FoldingSection, MemberRunKey>();
        foreach (var section in _foldingManager.AllFoldings)
        {
            if (!ReferenceEquals(section.Tag, RunFoldTag)) continue;
            if (RunOf(runs, section)?.Key is { } key)
            {
                matched.Add((section, key));
                keys[section] = key;
            }
            // Passed over, and its key kept: the next observation that finds it matching compares with this one.
            else if (_runKeys.TryGetValue(section, out var was))
                keys[section] = was;
        }

        foreach (var (section, key) in matched)
        {
            if (_runKeys.TryGetValue(section, out var was) && was != key)
                vm.ExpandedMemberRuns.Remove(was);
        }
        foreach (var (section, key) in matched)
        {
            if (section.IsFolded)
                vm.ExpandedMemberRuns.Remove(key);
            else
                vm.ExpandedMemberRuns.Add(key);
        }

        // Rebuilt from the sections the manager holds. One it has removed never comes back to be compared, so keeping
        // it would change nothing but the size of the map.
        _runKeys.Clear();
        foreach (var (section, key) in keys)
            _runKeys[section] = key;
    }

    /// <summary>
    /// The key each run section of this view was last seen under (task 4.3 review), so that
    /// <see cref="ObserveOwnFolds"/> can tell a key a run has moved off from one a deleted run left.
    /// </summary>
    /// <remarks>
    /// The view's, not the view model's: a section is this view's folding manager's, and a dock move makes them all
    /// anew. A run whose first sighting by a new view is after its rename keeps its old key in the set, which is the
    /// state before this was added.
    /// </remarks>
    private readonly Dictionary<FoldingSection, MemberRunKey> _runKeys = [];

    /// <summary>The header's folding section, or null when none is shown.</summary>
    private FoldingSection? HeaderSection()
    {
        if (_foldingManager is null) return null;
        foreach (var section in _foldingManager.GetFoldingsAt(0))
        {
            if (ReferenceEquals(section.Tag, HeaderFoldTag))
                return section;
        }
        return null;
    }

    /// <summary>The folding section at <paramref name="run"/>'s fold's offsets, whoever made it, or null.</summary>
    private FoldingSection? RunSection(MemberRun run)
    {
        if (_foldingManager is null) return null;
        foreach (var section in _foldingManager.GetFoldingsAt(run.Anchor))
        {
            if (section.EndOffset == run.LastContentEnd)
                return section;
        }
        return null;
    }

    /// <summary>The run describing a member whose fold is at <paramref name="section"/>'s offsets, or null.</summary>
    private static MemberRun? RunOf(IReadOnlyList<MemberRun> runs, FoldingSection section)
    {
        foreach (var run in runs)
        {
            if (run.DescribesMember && run.Anchor == section.StartOffset && run.LastContentEnd == section.EndOffset)
                return run;
        }
        return null;
    }

    /// <summary>
    /// True when every fold of the window's own is shown where it belongs, and no other: the header's, and one for
    /// each run that describes a member.
    /// </summary>
    private bool OwnFoldsAreCurrent(CodeEditorViewModel vm)
    {
        if (_foldingManager is null) return true;
        var header = Vb6FoldingAdapter.Header(vm.Document, vm.ReadOnlyRegionsNow);
        var headerSection = HeaderSection();
        var headerIsCurrent = header is null
            ? headerSection is null
            : headerSection is not null && headerSection.StartOffset == header.StartOffset
              && headerSection.EndOffset == header.EndOffset;
        if (!headerIsCurrent) return false;

        var runs = vm.MemberRunsNow;
        var shown = 0;
        foreach (var section in _foldingManager.AllFoldings)
        {
            if (!ReferenceEquals(section.Tag, RunFoldTag)) continue;
            if (RunOf(runs, section) is null) return false;
            shown++;
        }
        var wanted = 0;
        foreach (var run in runs)
        {
            if (run.DescribesMember) wanted++;
        }
        return shown == wanted;
    }

    /// <summary>
    /// Records the state of the window's own folds before an edit group's first change, which may remove them: a
    /// whole-document assignment removes every fold, and the folding manager unfolds each one it removes.
    /// </summary>
    /// <remarks>
    /// <b>Once per group, not at every change</b> (task 4.3 review). Reading the runs is a scan of the whole buffer,
    /// cached per version, and every change makes a new version, so recording at each <c>Changing</c> cost one scan
    /// per change: a format applies one change per line it rewrites, all in one group, and measured headlessly 8000
    /// line edits of a 165,230-character module took 99 seconds. A later change in the group would find the states
    /// the first found, since nothing the developer does runs between two changes of one group; all that can differ
    /// is the key a section is under, which the next observation reads: before the folds are re-made, or at the next
    /// group or the detach when they are still current.
    /// AvaloniaEdit raises <c>UpdateStarted</c> once per group, a bare <c>Replace</c> and an Undo included, since
    /// each opens a group of its own.
    /// </remarks>
    private void OnDocumentChangingForFolding(object? sender, DocumentChangeEventArgs e)
    {
        if (_foldsObservedThisGroup || DataContext is not CodeEditorViewModel vm) return;
        _foldsObservedThisGroup = true;
        ObserveOwnFolds(vm);
    }

    private void OnDocumentUpdateStartedForFolding(object? sender, EventArgs e) => _foldsObservedThisGroup = false;

    /// <summary>True once this edit group's first change has recorded the folds; see <see cref="OnDocumentChangingForFolding"/>.</summary>
    private bool _foldsObservedThisGroup;

    /// <summary>
    /// The caret's offset when a key that steps right went down, while that key is handled, or -1 (#273 task 4.3).
    /// Set by <see cref="OnEditorKeyDown"/>, ahead of the key's own handling, and cleared by
    /// <see cref="OnEditorKeyHandled"/> after it.
    /// </summary>
    /// <remarks>
    /// Its life is the key's and not until the caret next moves, which is what it first was (task 4.3 review): a
    /// step that moves nothing, because something took the key before the text area did, raises no caret move, and
    /// the next landing at the end of a folded run, whatever made it, went past the run as the step would have.
    /// </remarks>
    private int _stepRightFrom = -1;

    /// <summary>
    /// The caret-navigation commands that step to the right, by character or by word, moving the caret or extending
    /// the selection (task 4.3 review).
    /// </summary>
    /// <remarks>
    /// A step is told by the command its key runs and not by the key, because the keymap is the platform's: macOS
    /// binds Cmd+Right to the end of the line and Cmd+Shift+Right to selecting there, End and Shift+End and not steps,
    /// and told by the key, both went past the run from the declaration's end, where End stays put. Alt+Right is a word
    /// step there and nothing on Windows. The box-selection steps are left out: a rectangular selection moves the caret
    /// in virtual space, and none of them was measured against a folded run.
    /// </remarks>
    private static readonly HashSet<System.Windows.Input.ICommand> RightSteps =
    [
        EditingCommands.MoveRightByCharacter, EditingCommands.SelectRightByCharacter,
        EditingCommands.MoveRightByWord, EditingCommands.SelectRightByWord,
    ];

    /// <summary>
    /// True when <paramref name="e"/> is a key the text area's caret navigation runs a step to the right for, as it
    /// would choose: by the first of its key bindings whose gesture the key matches.
    /// </summary>
    private bool StepsRight(KeyEventArgs e)
    {
        foreach (var binding in TextEditor.TextArea.DefaultInputHandler.CaretNavigation.KeyBindings)
        {
            if (binding.Gesture?.Matches(e) == true)
                return binding.Command is { } command && RightSteps.Contains(command);
        }
        return false;
    }

    /// <summary>True while <see cref="KeepCaretOffFoldedRunsEnd"/> is placing the caret itself.</summary>
    /// <remarks>
    /// Defensive (task 4.3 review): the move it guards puts the caret at a run's anchor or at the start of
    /// the line after the run, and neither is ever the end of a run's fold, so the nested call would do nothing anyway.
    /// </remarks>
    private bool _placingCaret;

    /// <summary>
    /// Keeps the caret off the stop after a collapsed run's box, which is inside the run (#273 task 4.3): it goes to
    /// the box's other side, the end of the declaration, or past the run to the next line when a Right key moved it
    /// there from the declaration's end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A collapsed run gives its row two caret stops</b>, before the box and after it, and the second is the end
    /// of the run's last line (MidLineFoldBehaviourTests): where End, a click past the row's end, Up from a longer
    /// line below, Left and Ctrl+Left from the line after the run, and Right and Ctrl+Right from the declaration's end
    /// all put it. There, typing and Enter would be refused for no reason the developer can see, and F9 and the
    /// status bar would name the run's last line, which is not on screen. Moved to the box's first side, every
    /// one of them sees the end of the declaration the developer sees.
    /// </para>
    /// <para>
    /// <b>Except a step to the right from that end</b>, which would otherwise never get past the run: moved back to
    /// where it came from, Right would do nothing at all. The step is told apart by its key's command and not by where
    /// the caret came from, because End and a click land from the declaration's end exactly as Right does, and the
    /// position carries nothing to tell a click from a step. Runs from the caret's own <c>PositionChanged</c>, which
    /// AvaloniaEdit raises while it moves the caret and before it makes a selection from it, so Shift+Right selects to
    /// where the caret is put here.
    /// </para>
    /// <para>
    /// <b>Two of its checks are defensive</b> (task 4.3 review). The caret rests in a folded run's
    /// section only at one of its ends, because the folding manager, whose handler is installed with it and so runs
    /// before this one, unfolds a section the caret moves strictly inside; at the start there is nothing to do. And
    /// a step reaches the end of a folded run only from its anchor: Right from anywhere before it stops short, and a
    /// word step from inside the declaration's last token stops at the anchor, measured. The anchor is compared all
    /// the same, against a keymap whose step does otherwise.
    /// </para>
    /// </remarks>
    private void KeepCaretOffFoldedRunsEnd(object? sender, EventArgs e)
    {
        var stepFrom = _stepRightFrom;
        if (_placingCaret || _foldingManager is null || DataContext is not CodeEditorViewModel vm) return;

        var caret = TextEditor.TextArea.Caret.Offset;
        foreach (var section in _foldingManager.GetFoldingsContaining(caret))
        {
            if (!ReferenceEquals(section.Tag, RunFoldTag) || !section.IsFolded || section.EndOffset != caret) continue;
            var target = section.StartOffset;
            if (stepFrom == section.StartOffset && RunOf(vm.MemberRunsNow, section) is { } run && run.Region.End > caret)
                target = run.Region.End;
            _placingCaret = true;
            try
            {
                TextEditor.TextArea.Caret.Offset = target;
            }
            finally
            {
                _placingCaret = false;
            }
            return;
        }
    }

    /// <summary>
    /// Asks again now that a server has come up.
    /// </summary>
    /// <remarks>
    /// The request made when this view attached ran before any server had answered <c>initialize</c>, so
    /// the registry had no claimant to ask and returned nothing — and a document nobody types into never
    /// got a second chance (hexide-io/HexIDE#446). Every later transition re-asks too, which is what a
    /// reconnect or a server replaced in configuration needs, and costs one request that the scheduler
    /// below already coalesces.
    /// </remarks>
    private void OnLanguageServerStateChanged()
    {
        _semanticVersion++;
        if (DataContext is CodeEditorViewModel vm) ScheduleFolding(vm);
    }

    private void ScheduleFolding(CodeEditorViewModel vm)
    {
        _foldCts?.Cancel();
        _foldCts?.Dispose();
        _foldCts = new CancellationTokenSource();
        _ = FoldAfterDelayAsync(vm, _foldCts.Token);
    }

    private async Task FoldAfterDelayAsync(CodeEditorViewModel vm, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            await Task.WhenAll(RequestFoldingsAsync(vm, token), RequestSemanticTokensAsync(vm, token));
        }
        catch (OperationCanceledException) { }
    }

    private async Task RequestSemanticTokensAsync(CodeEditorViewModel vm, CancellationToken token)
    {
        var version = _semanticVersion;
        var colorizer = _semanticColorizer;
        if (colorizer is null) return;
        try
        {
            var result = await vm.RequestSemanticTokensAsync(token);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || version != _semanticVersion
                    || !ReferenceEquals(colorizer, _semanticColorizer) || !ReferenceEquals(DataContext, vm)) return;
                colorizer.Tokens = SemanticTokensColorizer.Decode(vm.Document, result);
                TextEditor.TextArea.TextView.Redraw();
            });
        }
        catch (OperationCanceledException) { }
    }

    private async Task RequestFoldingsAsync(CodeEditorViewModel vm, CancellationToken token)
    {
        if (_foldingManager is null) return;
        try
        {
            var ranges = await vm.RequestFoldingRangesAsync(token);
            if (token.IsCancellationRequested) return;
            var doc = TextEditor.Document;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _foldingManager is null) return;
                ApplyFolds(vm, Vb6FoldingAdapter.FromServer(doc, ranges));
                _stickyScroll?.Refresh();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[folding] {ErrorMessage}", ex.Message);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(TextEditor.TextArea.TextView);

        // Ignore sub-pixel jitter — only reset when the pointer moves > 4px
        var dx = Math.Abs(point.X - _lastHoverPoint.X);
        var dy = Math.Abs(point.Y - _lastHoverPoint.Y);
        if (_hoverCts != null && dx < 4 && dy < 4) return;

        _lastHoverPoint = point;
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = new CancellationTokenSource();
        var token = _hoverCts.Token;

        CloseTip();

        // GetPosition expects document coordinates, while pointer events are relative to the viewport.
        var tvPos = TextEditor.TextArea.TextView.GetPosition(point + TextEditor.TextArea.TextView.ScrollOffset);
        if (tvPos is null) return;

        if (_externalDebugger is { Stop: not null, SelectedFrame: not null })
        {
            if (_externalDebugger.CanEvaluateHover)
            {
                var word = WordAt(TextEditor.Document.GetOffset(tvPos.Value.Line, tvPos.Value.Column));
                if (!string.IsNullOrEmpty(word)) _ = ShowExternalDataTipAsync(word, token);
            }
            return;
        }

        // Break-mode Auto Data Tip: hovering an identifier while the debugger is Paused shows its LIVE value
        // (evaluated against the paused frame), taking precedence over LSP quick-info — VB6's Auto Data Tips.
        if (_debugController is { State: HexIDE.Runtime.Debugging.DebugState.Paused })
        {
            int offset = TextEditor.Document.GetOffset(tvPos.Value.Line, tvPos.Value.Column);
            _ = ShowDataTipAsync(offset, token);
            return;
        }

        var lspPos = new Position(tvPos.Value.Line - 1, tvPos.Value.Column - 1);
        if (DataContext is not CodeEditorViewModel vm) return;

        _ = HoverAfterDelayAsync(vm, lspPos, token);
    }

    private async Task ShowExternalDataTipAsync(string word, CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
            if (_externalDebugger is not { } debugger || token.IsCancellationRequested) return;
            var value = await debugger.EvaluateHoverAsync(word, token);
            if (token.IsCancellationRequested) return;
            if (value is null) { CloseTip(); return; }
            ToolTip.SetTip(TextEditor, $"{word} = {value}");
            ToolTip.SetIsOpen(TextEditor, true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Debug("[external datatip] {ErrorMessage}", ex.Message); }
    }

    // Evaluate the hovered identifier against the paused frame and show "identifier = value" as a data tip. Uses the
    // TYPED eval so only a genuine, in-scope value (Ok) produces a tip — a keyword or out-of-scope name yields no
    // tip rather than an error string. No-ops the moment the walk resumes (State re-checked after the delay/eval).
    private async Task ShowDataTipAsync(int offset, CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);   // match the quick-info dwell so tips don't flicker as the pointer sweeps
            var word = WordAt(offset);
            if (string.IsNullOrEmpty(word) || _debugController is not { State: HexIDE.Runtime.Debugging.DebugState.Paused } dc)
                return;

            var result = await dc.EvaluateWatchAsync(word);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var tip = token.IsCancellationRequested ? null : DataTipText(word, result);
                if (tip is null)
                {
                    CloseTip();
                    return;
                }
                ToolTip.SetTip(TextEditor, tip);
                ToolTip.SetIsOpen(TextEditor, true);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[datatip] {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>The data-tip text for a hovered identifier, or <c>null</c> to show NO tip. A tip appears only for a
    /// genuinely resolved, in-scope value (<see cref="HexIDE.Runtime.Debugging.DebugEvalResult.Ok"/>) — a keyword,
    /// an out-of-scope name, or an evaluation error yields no tip rather than an error string in the editor. Pure +
    /// internal so the show/suppress decision is unit-testable without a live view.</summary>
    internal static string? DataTipText(string? word, HexIDE.Runtime.Debugging.DebugEvalResult? result)
        => string.IsNullOrEmpty(word) || result is not { Ok: true } r ? null : $"{word} = {r.Display}";

    /// <summary>Closes the editor's tip and clears its text.</summary>
    /// <remarks>
    /// Clearing matters as much as closing. <c>SetIsOpen(false)</c> leaves <c>ToolTip.Tip</c> attached, so
    /// the control keeps advertising the LAST identifier's value long after that tip has gone — and this
    /// tip is dynamic, rebuilt per hover, unlike the static tips the property is designed for. Anything
    /// that reads the property then attributes the previous word's value to whatever is hovered now: the
    /// `hover` MCP tool reported "declared tip: total = 42" over `Debug.Print`, which is a wrong answer
    /// rather than a missing one. A real pointer can hit the same window, since ToolTipService may show
    /// what is attached before the replacement evaluation returns.
    /// </remarks>
    private void CloseTip()
    {
        ToolTip.SetIsOpen(TextEditor, false);
        ToolTip.SetTip(TextEditor, null);
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        // Forgotten, not just cancelled: a pointer coming back to the same point is a new hover, not jitter in the last one,
        // which the move handler took it for, so it opened no tip (#1265 review).
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = null;
        CloseTip();
    }

    public void TriggerQuickInfo()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = new CancellationTokenSource();
        _ = ShowHoverAsync(vm, lspPos, _hoverCts.Token);
    }

    public void ToggleFold()
    {
        if (_foldingManager is null) return;
        var caret = TextEditor.TextArea.Caret;
        var line = TextEditor.Document.GetLineByNumber(caret.Line);
        FoldingSection? target = null;
        foreach (var section in _foldingManager.AllFoldings)
        {
            if (!(section.StartOffset <= caret.Offset && caret.Offset <= section.EndOffset) &&
                !(line.Offset <= section.StartOffset && section.StartOffset <= line.EndOffset)) continue;
            if (target is null || section.StartOffset > target.StartOffset ||
                (section.StartOffset == target.StartOffset && section.EndOffset < target.EndOffset))
                target = section;
        }
        if (target is null) return;
        if (!target.IsFolded && target.StartOffset < caret.Offset && caret.Offset < target.EndOffset)
            caret.Offset = target.StartOffset;
        target.IsFolded = !target.IsFolded;
    }

    // ── Bookmarks ─────────────────────────────────────────────────────────────

    public void ToggleBookmark()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        vm.ToggleBookmark(TextEditor.TextArea.Caret.Line);
    }

    public void NextBookmark()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        int current = TextEditor.TextArea.Caret.Line - 1;
        var target = vm.BookmarkService.NextBookmark(vm.Identity, current);
        if (target.HasValue)
            JumpToBookmarkLine(target.Value);
    }

    public void PreviousBookmark()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        int current = TextEditor.TextArea.Caret.Line - 1;
        var target = vm.BookmarkService.PreviousBookmark(vm.Identity, current);
        if (target.HasValue)
            JumpToBookmarkLine(target.Value);
    }

    public void ClearAllBookmarks()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        vm.BookmarkService.ClearAll(vm.Identity);
    }

    private void JumpToBookmarkLine(int line)
    {
        int oneBased = line + 1;
        if (oneBased < 1 || oneBased > TextEditor.Document.LineCount) return;
        var docLine = TextEditor.Document.GetLineByNumber(oneBased);
        TextEditor.CaretOffset = docLine.Offset;
        TextEditor.ScrollToLine(oneBased);
        TextEditor.Focus();
    }

    private async Task HoverAfterDelayAsync(CodeEditorViewModel vm, Position pos, CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
            await ShowHoverAsync(vm, pos, token, vm.Settings.AutoQuickInfo);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ShowHoverAsync(CodeEditorViewModel vm, Position pos, CancellationToken token, bool requestQuickInfo = true)
    {
        try
        {
            ShowDiagnosticTip(pos, null);
            var result = requestQuickInfo
                ? await vm.RequestHoverAsync(pos, token) : null;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                ShowDiagnosticTip(pos, result?.Contents.Value);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[hover] {ErrorMessage}", ex.Message);
        }
    }

    private void ShowDiagnosticTip(Position pos, string? quickInfo)
    {
        var offset = TextEditor.Document.GetOffset(pos.Line + 1, pos.Character + 1);
        var parts = new List<string>();
        if (_markerService is { } service)
        {
            foreach (var marker in service.Markers)
            {
                if (offset >= marker.StartOffset && offset < marker.EndOffset)
                    parts.Add(string.IsNullOrEmpty(marker.Source)
                        ? marker.Message : $"{marker.Source}: {marker.Message}");
            }
        }
        if (!string.IsNullOrEmpty(quickInfo)) parts.Add(quickInfo);
        if (parts.Count == 0) { CloseTip(); return; }
        ToolTip.SetTip(TextEditor, string.Join(Environment.NewLine + Environment.NewLine, parts));
        ToolTip.SetIsOpen(TextEditor, true);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredSize = base.MeasureOverride(availableSize);

        return new Size(Math.Clamp(700, desiredSize.Width, availableSize.Width),
            Math.Clamp(380, desiredSize.Height, availableSize.Height));
    }

    // ── IntelliSense completion ──────────────────────────────────────────────

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        // Editing while the project is running/paused can't be hot-patched. Let the edit land (VB6 shows it), and
        // pop VB6's reset prompt — OK keeps the edit + resets the project, Cancel reverts it (see ShowResetPromptAsync).
        // Armed, not raised: whether the text lands is decided after this, by the read-only section provider.
        if (e.Text is { Length: > 0 })
            ArmResetPrompt();
        // Over a box selection the editor writes on each of its lines; written here so a fold on them cannot hang it.
        if (e.Text is { } boxed && TextEditor.TextArea.Selection is RectangleSelection { IsEmpty: false } box)
        {
            WriteOverBox(TextEditor.TextArea, box, boxed);
            e.Handled = true;
            _completionWindow?.Close();
            return;
        }
        // Shift+Enter, and any other input that types a line break, at the end of a described line (#273 task 4.5).
        // Taken here because AvaloniaEdit raises this before it writes and writes nothing once it is handled.
        if (e.Text is { } typed && PlaceTypedLineBreakPastRun(typed))
        {
            e.Handled = true;
            _completionWindow?.Close();
            return;
        }
        // If a completion window is open and the user types a character that can't
        // be part of an identifier, close the window (AvaloniaEdit will then insert the char).
        if (_completionWindow is null || e.Text is not { Length: > 0 }) return;
        var ch = e.Text[0];
        if (!char.IsLetterOrDigit(ch) && ch != '_')
            _completionWindow.Close();
    }

    /// <summary>
    /// Writes <paramref name="text"/> over a box selection as the editor's own typing does, on each of its lines, with
    /// any folded section on those lines opened for the write and folded again after it. AvaloniaEdit's box replacement
    /// rebuilds the box as each line changes, which loops for ever in the text view's height tree when a folded section
    /// starts on the box's lines, as a member's Attribute run folded under its declaration does (#1040 review).
    /// </summary>
    internal static void WriteOverBox(TextArea area, RectangleSelection box, string text)
    {
        var document = area.Document;
        var span = box.SurroundingSegment;
        var (first, last) = (document.GetLineByOffset(span.Offset), document.GetLineByOffset(span.EndOffset));
        var folds = area.TextView.GetService(typeof(FoldingManager)) as FoldingManager;
        var opened = folds?.AllFoldings
            .Where(f => f.IsFolded && f.StartOffset <= last.EndOffset && f.EndOffset >= first.Offset)
            .ToList() ?? [];
        foreach (var fold in opened)
            fold.IsFolded = false;
        try
        {
            box.ReplaceSelectionWithText(text);
        }
        finally
        {
            // A fold the write took away is no longer the manager's to fold.
            var kept = folds?.AllFoldings.ToHashSet();
            foreach (var fold in opened.Where(f => kept?.Contains(f) == true))
                fold.IsFolded = true;
        }
    }

    // VB6-faithful Edit-and-Continue affordance: the interpreter can't apply an edit to a running program, so a code
    // edit while running/paused pops VB6's "This action will reset your project" prompt. The edit is NOT cancelled
    // (VB6 lets it appear), and a "Cancel" answer reverts every edit made while the prompt was open.
    //
    // Two steps, because the input handlers run BEFORE anything decides whether the key writes (#273 task 3.13).
    // The read-only section provider refuses typing in the header after TextEntering, and the Enter handler refuses
    // a line break there after this view's KeyDown; asking from the input handlers asked the developer to reset a
    // run for a keystroke that wrote nothing. So input only ARMS the prompt, for as long as that input is being
    // handled, and the prompt opens on the document's own Changing event, which fires only when an edit lands, and
    // before it is applied, which is when the code is captured. A change nobody armed for is the IDE's own: a
    // header refresh, a reload. It never asks, which the code-editor capability requires of a designer change.
    private void ArmResetPrompt()
    {
        if (_resetPromptOpen || DataContext is not CodeEditorViewModel vm || !vm.IsProjectRunning)
            return;
        _resetPromptArmed = true;
        // Disarmed once this input has been handled. The edit it makes, if it makes one, is applied before then:
        // AvaloniaEdit writes typed text and edit keys synchronously inside the event that delivered them.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _resetPromptArmed = false);
    }

    private void OnDocumentChangingForResetPrompt(object? sender, DocumentChangeEventArgs e)
    {
        if (!_resetPromptArmed)
            return;
        _resetPromptArmed = false;
        StartResetPrompt();
    }

    /// <summary>
    /// Opens the prompt now, capturing the code as it stands, which must be before the edit it asks about lands.
    /// </summary>
    private void StartResetPrompt()
    {
        if (_resetPromptOpen || DataContext is not CodeEditorViewModel vm || !vm.IsProjectRunning)
            return;
        _resetPromptOpen = true;
        _preEditBody = vm.BufferBody;
        // Kept relative to the code, like the code itself: the header can change length before the answer comes.
        _preEditCaret = TextEditor.CaretOffset - vm.BufferPrefixLength;
        _ = ShowResetPromptAsync(vm);
    }

    /// <summary>
    /// Opens the prompt before a paste or a cut, when it would write anything.
    /// </summary>
    /// <remarks>
    /// These two cannot be armed like the other edits: they read or write the clipboard, which is asynchronous,
    /// so the edit lands after the command that asked for it has returned and the arm is gone. So whether they
    /// will write is decided before they run, by the same section provider AvaloniaEdit will consult. Measured
    /// before this existed: a paste into code while a run was paused landed with no prompt at all, by every
    /// route, because the command binding took the key before the handler that asked.
    /// </remarks>
    private void AskBeforeClipboardEdit(bool paste)
    {
        if (ClipboardEditWouldWrite(paste))
            StartResetPrompt();
    }

    /// <summary>
    /// Whether a paste or a cut about to run would write anything, judged by the section provider that will
    /// judge it. A cut with nothing selected takes the caret's whole line.
    /// </summary>
    private bool ClipboardEditWouldWrite(bool paste)
    {
        var area = TextEditor.TextArea;
        var provider = area.ReadOnlySectionProvider;
        var selection = area.Selection.SurroundingSegment;
        if (paste && selection is not { Length: > 0 })
            return provider.CanInsert(area.Caret.Offset);

        ISegment span = selection is { Length: > 0 }
            ? selection
            : area.Document.GetLineByOffset(area.Caret.Offset);
        return provider.GetDeletableSegments(span).Any();
    }

    private async Task ShowResetPromptAsync(CodeEditorViewModel vm)
    {
        try
        {
            // Over a minimized window, an owned window does not show, and the prompt could not be answered.
            var reset = await vm.ConfirmResetWhileRunningAsync(
                TopLevel.GetTopLevel(this) is Window { IsVisible: true, WindowState: not WindowState.Minimized } window ? window : null);
            // Cancel → undo every edit made since the prompt opened, back to the pre-edit code. OK → keep the edit
            // (the run reset was already requested), matching VB6. The code alone goes back, never the whole
            // buffer: the header may have been rewritten while the prompt was open (see RevertBodyTo).
            if (!reset && _preEditBody is { } body)
            {
                vm.RevertBodyTo(body);
                TextEditor.CaretOffset = System.Math.Clamp(
                    vm.BufferPrefixLength + _preEditCaret, 0, TextEditor.Document.TextLength);
            }
        }
        finally
        {
            _resetPromptOpen = false;
            _preEditBody = null;
        }
    }

    // Follow the library's editing keys; an arm costs nothing unless a document change lands.
    // Clipboard keys have their asynchronous path below. Undo/Redo remain unmeasured (#710).
    private bool IsDocumentEditKey(KeyEventArgs e)
    {
        foreach (var binding in TextEditor.TextArea.DefaultInputHandler.Editing.KeyBindings)
        {
            if (binding.Gesture?.Matches(e) == true)
                return binding.Command != AvaloniaEdit.ApplicationCommands.Undo
                    && binding.Command != AvaloniaEdit.ApplicationCommands.Redo;
        }
        return AvaloniaEditCommands.DeleteLine.Gesture?.Matches(e) == true
            || AvaloniaEditCommands.IndentSelection.Gesture?.Matches(e) == true;
    }

    /// <summary>
    /// True when <paramref name="e"/> is the text area's own Paste, false when it is its own Cut, and null for any
    /// other key: by the first of its editing bindings the key matches, as <see cref="DeletesTheSelection"/> tells
    /// a deletion.
    /// </summary>
    /// <remarks>
    /// The platform places these keys, Ctrl+V and Ctrl+X on Windows and Linux and Cmd on macOS. This used to match
    /// Ctrl alone, so on macOS a paste or a cut by the library's own key wrote into a running program's code with no
    /// prompt: CI's macOS run found it, in <c>ResetPromptTests</c> (#273).
    /// </remarks>
    private bool? ClipboardEditOf(KeyEventArgs e)
    {
        foreach (var binding in TextEditor.TextArea.DefaultInputHandler.Editing.KeyBindings)
        {
            if (binding.Gesture?.Matches(e) != true)
                continue;
            if (binding.Command == AvaloniaEdit.ApplicationCommands.Paste)
                return true;
            return binding.Command == AvaloniaEdit.ApplicationCommands.Cut ? false : null;
        }
        return null;
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: > 0 }) return;
        var ch = e.Text[0];
        // Trigger completion on any letter (start/continuation of a word)
        if (char.IsLetter(ch))
            TriggerCompletionAsync();
        else if (DataContext is CodeEditorViewModel vm && vm.CompletionTriggerCharacters.Contains(e.Text))
            TriggerCompletionAsync(e.Text);
        // Trigger signature help on ( or ,
        if (ch == '(' || ch == ',')
            TriggerSignatureHelpAsync(ch.ToString());
        // Close signature help when all parens are closed
        if (ch == ')')
            CloseInsightWindowIfBalanced();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Ahead of AvaloniaEdit's own handling, which moves the caret: a step to the right from the end of a
        // declaration whose run is folded goes past the run, where End or a click landing on the same stop do not
        // (KeepCaretOffFoldedRunsEnd, #273 task 4.3). A step is a key the caret navigation steps right for, whatever
        // key the platform puts it on, and it lasts until OnEditorKeyHandled, after the key has been handled.
        _stepRightFrom = StepsRight(e) ? TextEditor.CaretOffset : -1;

        // Editing while the project is running/paused → VB6 reset prompt (see ArmResetPrompt). Covers the
        // library editing keys that reach the text area; printable chars go via
        // OnTextEntering, and Delete, Paste and Cut via the commands their keys are bound to.
        // The edit is allowed to proceed (VB6 lets it land); Cancel reverts it, OK keeps it + resets the project.
        // The keymap's Paste and Cut keys normally never get here, because the code window's command bindings take
        // them; this is for a key the keymap leaves on AvaloniaEdit's own Paste or Cut, wherever the platform puts it.
        if (ClipboardEditOf(e) is { } paste)
            AskBeforeClipboardEdit(paste);
        else if (IsDocumentEditKey(e))
            ArmResetPrompt();
        // A key that deletes the selection, ahead of the library that deletes it: a deletion of a described line
        // takes the line's attribute run with it (#273 task 4.5a). Delete itself is the Delete command's, which does
        // the same. Ctrl+D, the library's Delete Line, is taken over only when it would leave a run behind.
        if (DeletesTheSelection(e))
            WidenSelectionForDeletion(wholeLineWhenEmpty: false);
        else if (AvaloniaEditCommands.DeleteLine.Gesture?.Matches(e) == true && DeleteLinesWithTheirRuns())
        {
            e.Handled = true;
            return;
        }
        // Enter = auto-close block + auto-indent
        if (e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            // Over a selection, Enter writes over it, as typing does and as Shift+Enter did (#1040): the selection goes,
            // and the line break goes where it was, as one change. Typing is not a deletion, so a described line's run is
            // not taken with it. A selection reaching a region only the IDE may change takes nothing, as typing there does.
            var newline = DocumentNewLines.Of(TextEditor.Document.Text);
            using (TextEditor.Document.RunUpdate())
            {
                var removed = false;
                // A box's text goes first, each line's as the read-only sections allow, and Enter is then the caret's.
                // The editor's own would write the line break over the box as a paste of two lines, one to a line.
                if (TextEditor.TextArea.Selection is RectangleSelection { IsEmpty: false } box)
                {
                    WriteOverBox(TextEditor.TextArea, box, string.Empty);
                    TextEditor.TextArea.ClearSelection();
                    removed = true;
                }
                if (EditedSpan() is var (offset, length) && length > 0)
                {
                    var vm = DataContext as CodeEditorViewModel;
                    if (vm is not null && vm.RefusesTyping(offset, length))
                    {
                        e.Handled = true;
                        return;
                    }
                    // At the end of a described line the break goes after the run, as Shift+Enter's does: placed with
                    // the span it replaces in hand, since taking the selection first would leave the run under nothing.
                    // A selection within one line that leaves code before it leaves the line its run hangs from, so it
                    // goes first and Enter is then the caret's, re-casing the line and closing its block as at its end.
                    var line = TextEditor.Document.GetLineByOffset(offset);
                    var keepsLine = offset + length <= line.EndOffset
                                    && !string.IsNullOrWhiteSpace(TextEditor.Document.GetText(line.Offset, offset - line.Offset));
                    var indent = VbAutoCloseProvider.GetIndentation(TextEditor.Document.GetText(line.Offset, line.Length));
                    if (!keepsLine && vm?.InsertLineBreakPastRun(offset, length, "\n" + indent) is { } placed)
                    {
                        // The replaced text's selection would otherwise carry over onto the line break.
                        TextEditor.TextArea.ClearSelection();
                        TextEditor.TextArea.Caret.Offset = placed.End;
                        e.Handled = true;
                        return;
                    }
                    TextEditor.TextArea.Selection.ReplaceSelectionWithText(string.Empty);
                    removed = true;
                }
                if (HandleEnterAutoClose(newline))
                    e.Handled = true;
                else if (removed)
                {
                    // Auto-close failed: the line break still goes in this change, not as the editor's second one.
                    TextEditor.Document.Insert(TextEditor.CaretOffset, newline);
                    e.Handled = true;
                }
            }
        }
        // F12 = go to definition
        else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            GoToDefinition();
        }
        // Ctrl+F12 = go to declaration, next to F12 because it answers the neighbouring question
        else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            GoToDeclaration();
        }
        // Shift+Alt+F = format document
        else if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Shift | KeyModifiers.Alt))
        {
            e.Handled = true;
            FormatDocument();
        }
    }

    /// <summary>
    /// Ends the step <see cref="OnEditorKeyDown"/> noted, once the key has been handled, whoever handled it (#273 task
    /// 4.3 review).
    /// </summary>
    private void OnEditorKeyHandled(object? sender, KeyEventArgs e)
    {
        _stepRightFrom = -1;
        if (e.Key == Key.Insert && DataContext is CodeEditorViewModel vm)
            vm.StatusBar.InsertMode = !TextEditor.TextArea.OverstrikeMode;
    }

    /// <summary>
    /// Handles Enter key press: if the current logical line is a VB6 block opener,
    /// inserts the closing statement and positions the cursor between them.
    /// Otherwise, inserts a newline with auto-indent (matching current line).
    /// </summary>
    /// <returns>True if the event was handled.</returns>
    /// <remarks>
    /// <b>At the end of a described line the line break goes after the run</b> (#273 task 4.5), as a line of its own
    /// in the document's newline, and the closer is looked for past the run, all as one undo step with the
    /// declaration's re-casing. Put at the caret, the line break would leave the run hanging from an empty line,
    /// describing nothing. The declaration is read as 4.1's reader reads it, from <see cref="MemberRun.DescribedStart"/>:
    /// its continuation rule, an underscore after whitespace, and not <see cref="VbAutoCloseProvider.IsContinuationLine"/>'s
    /// looser one, which would take a declaration ending in a name like <c>Total_</c> for one continuing into the run,
    /// and break the line at the caret.
    /// </remarks>
    private bool HandleEnterAutoClose(string newline)
    {
        try
        {
            var doc = TextEditor.Document;
            var caret = TextEditor.TextArea.Caret;
            var caretOffset = caret.Offset;

            // Enter inside the header or a member's attribute lines writes nothing (#273 task 3.9), and nor
            // does Enter anywhere in a file held read-only as a whole, whose banner says typing is refused. It
            // is taken here, ahead of AvaloniaEdit's own Enter, so no read-only section provider would ever see
            // it; and there is no "after the region" a line break could sensibly go to instead.
            var vm = DataContext as CodeEditorViewModel;
            if (vm is not null && vm.RefusesTyping(caretOffset, 0))
                return true;

            // The run hanging from the line the caret ends, when it ends one, with only whitespace after the caret.
            var run = vm?.ReadOnlySections.RunHangingFrom(caretOffset);

            var currentLineNumber = doc.GetLineByOffset(caretOffset).LineNumber;
            var currentDocLine = doc.GetLineByNumber(currentLineNumber);
            var currentLineText = doc.GetText(currentDocLine.Offset, currentDocLine.Length);

            Log.Debug("[auto-close] Line {LineNumber}: '{LineText}', caretOffset={Offset}, lineEnd={EndOffset}",
                currentLineNumber, currentLineText, caretOffset, currentDocLine.EndOffset);

            // If caret is not at end of line content (ignoring trailing whitespace),
            // just do basic auto-indent — don't auto-close mid-line
            var textAfterCaret = doc.GetText(caretOffset, currentDocLine.EndOffset - caretOffset);
            if (textAfterCaret.Trim().Length > 0)
            {
                Log.Debug("[auto-close] Caret not at end of line, text after: '{TextAfter}'", textAfterCaret);
                InsertNewlineWithIndent(doc, caretOffset, currentLineText, newline);
                return true;
            }

            // If the line ends with _ (continuation), just auto-indent (no case-correct yet). Never a described
            // line: the reader ended its logical line there, or no run would hang from it.
            if (run is null && VbAutoCloseProvider.IsContinuationLine(currentLineText))
            {
                Log.Debug("[auto-close] Continuation line, auto-indent only");
                InsertNewlineWithIndent(doc, caretOffset, currentLineText, newline);
                return true;
            }

            // Find the first physical line of the logical line (for continuations)
            var startLineNumber = currentLineNumber;
            if (run is not null)
                startLineNumber = doc.GetLineByOffset(run.DescribedStart).LineNumber;
            else
            {
                while (startLineNumber > 1)
                {
                    var prevLine = doc.GetLineByNumber(startLineNumber - 1);
                    if (!VbAutoCloseProvider.IsContinuationLine(
                            doc.GetText(prevLine.Offset, prevLine.Length)))
                        break;
                    startLineNumber--;
                }
            }

            // Assemble the full logical line (handle multi-line continuations). Nothing above its first line is
            // offered, so for a described line the looser rule cannot walk back past where 4.1's reader starts it.
            var logicalLine = VbAutoCloseProvider.AssembleLogicalLine(
                lineIdx =>
                {
                    if (lineIdx + 1 < startLineNumber)
                        return string.Empty;
                    var dl = doc.GetLineByNumber(lineIdx + 1); // lineIdx is 0-based, doc is 1-based
                    return doc.GetText(dl.Offset, dl.Length);
                },
                currentLineNumber - 1); // Convert to 0-based index

            var closer = VbAutoCloseProvider.GetClosingStatement(logicalLine);
            Log.Debug("[auto-close] Logical line: '{LogicalLine}', closer: {Closer}",
                logicalLine, closer ?? "(null)");

            // A run is folded into the line it describes as the developer sees it, so the closer is looked for after it.
            var lastLineBefore = run is null ? currentLineNumber : doc.GetLineByOffset(run.LastContentEnd).LineNumber;

            // Keyword-normalize the committed physical lines + insert closer (single undo)
            doc.BeginUpdate();
            try
            {
                // 1. Case-correct all physical lines of the committed logical line
                var offsetAdjustment = 0;
                for (var lineNum = startLineNumber; lineNum <= currentLineNumber; lineNum++)
                {
                    var dl = doc.GetLineByNumber(lineNum);
                    // A committed line inside a region keeps its casing: re-casing it is a rewrite of text
                    // only the IDE may change, and the insert rule does not cover rewriting a line.
                    if (vm is not null && vm.IsReadOnlyRegion(dl.Offset, dl.Length))
                        continue;
                    var lineText = doc.GetText(dl.Offset, dl.Length);
                    var normalized = VbKeywordNormalizer.NormalizeLine(lineText);
                    if (normalized is not null)
                    {
                        doc.Replace(dl.Offset, dl.Length, normalized);
                        offsetAdjustment += normalized.Length - lineText.Length;
                    }
                }

                // Recalculate caret offset after normalization (line lengths may change)
                var adjustedCaretOffset = caretOffset + offsetAdjustment;

                var updatedLine = doc.GetLineByNumber(currentLineNumber);
                var updatedLineText = doc.GetText(updatedLine.Offset, updatedLine.Length);
                var indent = VbAutoCloseProvider.GetIndentation(updatedLineText);
                var bodyIndent = VbAutoCloseProvider.GetBodyIndent(indent);
                var addCloser = closer is not null && !NextNonEmptyLineIs(doc, lastLineBefore, closer);
                var insertText = addCloser ? newline + bodyIndent + newline + indent + closer : newline + indent;

                // After the run: the provider places it, writing each line break as the described line's own. The
                // caret goes to the end of the new line, or to the body line between the declaration and the closer.
                if (run is not null && vm?.InsertLineBreakPastRun(adjustedCaretOffset, 0, insertText) is { } placed)
                {
                    var lineBreak = placed.Tail.StartsWith("\r\n", StringComparison.Ordinal) ? 2 : 1;
                    caret.Offset = addCloser ? placed.TailStart + lineBreak + bodyIndent.Length : placed.End;
                    return true;
                }

                if (!addCloser)
                {
                    // No closer to insert — just newline + auto-indent
                    doc.Insert(adjustedCaretOffset, insertText);
                    caret.Offset = adjustedCaretOffset + insertText.Length;
                }
                else
                {
                    // Insert closer
                    Log.Debug("[auto-close] Inserting closer: indent='{Indent}', insertText='{InsertText}'",
                        indent, insertText.Replace("\n", "\\n"));

                    doc.Insert(adjustedCaretOffset, insertText);
                    caret.Offset = adjustedCaretOffset + newline.Length + bodyIndent.Length;
                }
            }
            finally
            {
                doc.EndUpdate();
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[auto-close] Error in HandleEnterAutoClose");
            return false;
        }
    }

    private void InsertNewlineWithIndent(TextDocument doc, int caretOffset, string currentLineText, string newline)
    {
        var indent = VbAutoCloseProvider.GetIndentation(currentLineText);
        var insertText = newline + indent;
        doc.Insert(caretOffset, insertText);
        TextEditor.TextArea.Caret.Offset = caretOffset + insertText.Length;
    }

    /// <summary>
    /// Checks whether the next non-empty line after <paramref name="currentLineNumber"/>
    /// contains the expected closing statement (case-insensitive, ignoring whitespace).
    /// </summary>
    private static bool NextNonEmptyLineIs(TextDocument doc, int currentLineNumber, string closer)
    {
        for (var i = currentLineNumber + 1; i <= doc.LineCount; i++)
        {
            var line = doc.GetLineByNumber(i);
            var text = doc.GetText(line.Offset, line.Length).Trim();
            if (text.Length == 0)
                continue;
            return string.Equals(text, closer, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // ── A member's run follows its line (#273 tasks 4.5 and 4.5a) ─────────────────────────────────────────

    /// <summary>
    /// The selection, or the caret as an empty one: where an edit made with the keyboard starts and how much it
    /// replaces. Null for a rectangular selection, which the library writes line by line and these rules leave to it.
    /// </summary>
    private (int Offset, int Length)? EditedSpan()
    {
        var area = TextEditor.TextArea;
        if (area.Selection is RectangleSelection)
            return null;
        if (area.Selection.IsEmpty)
            return (area.Caret.Offset, 0);
        var selected = area.Selection.SurroundingSegment;
        return (selected.Offset, selected.Length);
    }

    /// <summary>
    /// Widens the selection over the attribute run of every described line whose whole text deleting it would remove,
    /// so the library deletes the run with its line, as one change and one undo step, and a cut puts both on the
    /// clipboard (task 4.5a). With nothing selected, a whole-line cut's line is what is widened.
    /// </summary>
    /// <remarks>
    /// Here, before the library asks the provider, because the provider can only narrow what it is asked about:
    /// AvaloniaEdit throws on a segment outside the span. The selection is left as it was when nothing is widened.
    /// That only keeps it from being replaced by an equal one: the library deletes the same text either way, and its
    /// whole-line cut of an empty selection copies the same line as a cut of that line selected (equivalent under
    /// mutation, #273 task 4.5a review).
    /// </remarks>
    private void WidenSelectionForDeletion(bool wholeLineWhenEmpty)
    {
        if (DataContext is not CodeEditorViewModel vm || EditedSpan() is not var (offset, length))
            return;
        var area = TextEditor.TextArea;
        if (length == 0)
        {
            if (!wholeLineWhenEmpty)
                return;
            var line = area.Document.GetLineByOffset(offset);
            (offset, length) = (line.Offset, line.TotalLength);
        }
        var widened = vm.ReadOnlySections.WidenDeletion(offset, length);
        if (widened.Length != length)
            area.Selection = Selection.Create(area, widened.Offset, widened.Offset + widened.Length);
    }

    /// <summary>
    /// The library's commands that delete a selection when there is one, which the keys reaching the text area run:
    /// Backspace, and Delete or Backspace by word. Delete alone is the code window's Delete command.
    /// </summary>
    private static readonly HashSet<System.Windows.Input.ICommand> SelectionDeletes =
    [
        EditingCommands.Backspace, EditingCommands.Delete,
        EditingCommands.DeletePreviousWord, EditingCommands.DeleteNextWord,
    ];

    /// <summary>
    /// True when <paramref name="e"/> is a key the text area deletes the selection for, by the first of its editing
    /// bindings the key matches, as <see cref="StepsRight"/> tells a step; false with nothing selected.
    /// </summary>
    private bool DeletesTheSelection(KeyEventArgs e)
    {
        if (TextEditor.TextArea.Selection.IsEmpty)
            return false;
        foreach (var binding in TextEditor.TextArea.DefaultInputHandler.Editing.KeyBindings)
        {
            if (binding.Gesture?.Matches(e) == true)
                return binding.Command is { } command && SelectionDeletes.Contains(command);
        }
        return false;
    }

    /// <summary>
    /// Ctrl+D, the library's Delete Line, when the lines it would delete include a described line: it deletes them
    /// with that line's run instead, as one change. False, with nothing done, when no run would be left behind, so
    /// the library's own deletes the lines as before.
    /// </summary>
    /// <remarks>
    /// Its own, because the library's computes the lines from the selection's two ends, and a selection widened to
    /// the line after the run would delete that line too.
    /// </remarks>
    private bool DeleteLinesWithTheirRuns()
    {
        var area = TextEditor.TextArea;
        if (DataContext is not CodeEditorViewModel vm || area.Selection is RectangleSelection)
            return false;
        var (first, last) = area.Selection.Length == 0
            ? (area.Caret.Line, area.Caret.Line)
            : (Math.Min(area.Selection.StartPosition.Line, area.Selection.EndPosition.Line),
               Math.Max(area.Selection.StartPosition.Line, area.Selection.EndPosition.Line));
        var from = area.Document.GetLineByNumber(first).Offset;
        var lastLine = area.Document.GetLineByNumber(last);
        var length = lastLine.Offset + lastLine.TotalLength - from;
        var widened = vm.ReadOnlySections.WidenDeletion(from, length);
        if (widened.Length == length)
            return false;

        area.Selection = Selection.Create(area, widened.Offset, widened.Offset + widened.Length);
        TextEditor.Delete();
        return true;
    }

    /// <summary>True when what a paste would replace ends where a member's run hangs (task 4.5).</summary>
    private bool SelectionEndsWhereARunHangs() =>
        DataContext is CodeEditorViewModel vm && EditedSpan() is var (offset, length)
        && vm.ReadOnlySections.RunHangingFrom(offset + length) is not null;

    /// <summary>
    /// A paste at the end of a described line: the clipboard's text up to its first line break where the caret is,
    /// and the rest after the run, as one undo step (task 4.5). Text with no line break is pasted where it was asked.
    /// </summary>
    /// <remarks>
    /// The text is made what the library's paste makes it, with tabs as spaces when the editor converts them, and the
    /// selection is read again once the clipboard has answered, since the developer may have moved on.
    /// </remarks>
    private async Task PasteAtTheEndOfADescribedLineAsync()
    {
        var text = await ClipboardTextAsync();
        if (string.IsNullOrEmpty(text) || DataContext is not CodeEditorViewModel vm)
            return;

        var area = TextEditor.TextArea;
        if (area.Options.ConvertTabsToSpaces)
            text = text.Replace("\t", new string(' ', area.Options.IndentationSize));
        if (EditedSpan() is var (offset, length) && vm.InsertLineBreakPastRun(offset, length, text) is { } placed)
        {
            area.ClearSelection();
            area.Caret.Offset = placed.End;
        }
        else
        {
            area.Selection.ReplaceSelectionWithText(
                TextUtilities.NormalizeNewLines(text, TextUtilities.GetNewLineFromDocument(area.Document, area.Caret.Line)));
        }
        area.Caret.BringCaretToView();
    }

    /// <summary>
    /// A paste over a box selection, written by <see cref="WriteOverBox"/> as typing over the box is: the library's paste
    /// replaces the box with the folds on its lines closed, and a folded run there hangs it (#1251).
    /// </summary>
    /// <remarks>
    /// The text is made what the library's paste makes it, in the module's newline and with tabs as spaces when the
    /// editor converts them. The selection is read again once the clipboard has answered, and one that is no longer a
    /// box is written over as the library's paste would write it.
    /// </remarks>
    private async Task PasteOverBoxAsync()
    {
        var text = await ClipboardTextAsync();
        if (string.IsNullOrEmpty(text))
            return;

        var area = TextEditor.TextArea;
        text = TextUtilities.NormalizeNewLines(text, TextUtilities.GetNewLineFromDocument(area.Document, area.Caret.Line));
        if (area.Options.ConvertTabsToSpaces)
            text = text.Replace("\t", new string(' ', area.Options.IndentationSize));
        if (area.Selection is RectangleSelection { IsEmpty: false } box)
            WriteOverBox(area, box, text);
        else
            area.Selection.ReplaceSelectionWithText(text);
        area.Caret.BringCaretToView();
    }

    private async Task PasteAsCommentAsync()
    {
        var text = await ClipboardTextAsync();
        if (string.IsNullOrEmpty(text))
            return;
        AskBeforeClipboardEdit(paste: true);
        InsertAsComment(text);
    }

    internal void InsertAsComment(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var area = TextEditor.TextArea;
        var line = area.Document.GetLineByOffset(TextEditor.SelectionStart);
        var prefix = area.Document.GetText(line.Offset, TextEditor.SelectionStart - line.Offset);
        var indentation = new string(prefix.TakeWhile(c => c is ' ' or '\t').ToArray());
        var newline = TextUtilities.GetNewLineFromDocument(area.Document, line.LineNumber);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var continuationIndent = area.Selection is RectangleSelection { IsEmpty: false } ? "" : indentation;
        var commented = "'" + string.Join(newline + continuationIndent + "'", lines);
        if (EditedSpan() is var (selectionOffset, selectionLength)
            && selectionOffset + selectionLength < area.Document.GetLineByOffset(selectionOffset + selectionLength).EndOffset)
            commented += newline + indentation;
        using (area.Document.RunUpdate())
        {
            if (area.Selection is RectangleSelection { IsEmpty: false } box)
                WriteOverBox(area, box, commented);
            else if (DataContext is CodeEditorViewModel vm
                && EditedSpan() is var (offset, length)
                && vm.InsertLineBreakPastRun(offset, length, commented) is { } placed)
            {
                area.ClearSelection();
                area.Caret.Offset = placed.End;
            }
            else
                area.Selection.ReplaceSelectionWithText(commented);
        }
        area.Caret.BringCaretToView();
    }

    /// <summary>The clipboard's text, or null when there is none or it cannot be read.</summary>
    private async Task<string?> ClipboardTextAsync()
    {
        try
        {
            return TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        }
        catch (Exception ex)
        {
            Log.Debug("[paste] {ErrorMessage}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Writes typed text holding a line break past the run when it lands at the end of a described line (task 4.5):
    /// Shift+Enter, and Enter when the window's own handling of it has failed, both of which the library types.
    /// </summary>
    /// <returns>True when it was written here, and the library must write nothing.</returns>
    /// <remarks>
    /// A line break alone is a key's, and the library indents the line it makes like the line above it. The line
    /// above the new one is the declaration's last physical line, as the developer sees it, so the new line takes
    /// that line's indent, which is what it would take with no run there: for a continued declaration, the
    /// continuation's and not the first line's (#273 task 4.5 review).
    /// </remarks>
    private bool PlaceTypedLineBreakPastRun(string typed)
    {
        if (typed.AsSpan().IndexOfAny('\r', '\n') < 0 || DataContext is not CodeEditorViewModel vm
            || EditedSpan() is not var (offset, length)
            || vm.ReadOnlySections.RunHangingFrom(offset + length) is not { } run)
            return false;

        if (typed is "\n" or "\r" or "\r\n")
        {
            var declaration = TextEditor.Document.GetLineByOffset(run.Anchor);
            typed += VbAutoCloseProvider.GetIndentation(TextEditor.Document.GetText(declaration));
        }
        if (vm.InsertLineBreakPastRun(offset, length, typed) is not { } placed)
            return false;

        var area = TextEditor.TextArea;
        area.ClearSelection();
        area.Caret.Offset = placed.End;
        area.Caret.BringCaretToView();
        return true;
    }

    private void TriggerCompletionAsync(string? triggerCharacter = null)
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        if (!vm.Settings.AutoListMembers) return;

        _completionCts?.Cancel();
        _completionCts?.Dispose();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;

        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _completionShown = ShowCompletionAsync(vm, lspPos, token, triggerCharacter);
    }

    private async Task ShowCompletionAsync(CodeEditorViewModel vm, Position position, CancellationToken token,
        string? triggerCharacter)
    {
        try
        {
            // The document change is debounced 300ms, but completion needs the current source.
            await vm.FlushDocumentAsync(token);
            if (token.IsCancellationRequested) return;

            var items = triggerCharacter is null
                ? await vm.RequestCompletionAsync(position, token)
                : await vm.RequestCompletionForTriggerAsync(position, triggerCharacter, token);
            if (token.IsCancellationRequested || items.Length == 0) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                // If a window is already open, close it first
                _completionWindow?.Close();

                _completionWindow = new CompletionWindow(TextEditor.TextArea);
                var data = _completionWindow.CompletionList.CompletionData;
                foreach (var item in items)
                    data.Add(new VbCompletionData(item, vm.RefusesTyping));

                _completionWindow.Closed += (_, _) => _completionWindow = null;
                _completionWindow.Show();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[completion] {ErrorMessage}", ex.Message);
        }
    }

    // ── Signature help ───────────────────────────────────────────────────────

    private void TriggerSignatureHelpAsync(string? triggerCharacter = null)
    {
        if (DataContext is not CodeEditorViewModel vm) return;

        _signatureCts?.Cancel();
        _signatureCts?.Dispose();
        _signatureCts = new CancellationTokenSource();
        var token = _signatureCts.Token;

        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _signatureShown = ShowSignatureHelpAsync(vm, lspPos, token, triggerCharacter);
    }

    private void CloseInsightWindowIfBalanced()
    {
        // Count parens from start of current line to caret; if depth == 0, no open call
        var doc = TextEditor.Document;
        var caret = TextEditor.TextArea.Caret;
        var lineObj = doc.GetLineByNumber(caret.Line);
        var lineText = doc.GetText(lineObj.Offset, caret.Offset - lineObj.Offset);
        int depth = 0;
        foreach (var c in lineText)
        {
            if (c == '(') depth++;
            else if (c == ')') depth--;
        }
        if (depth <= 0)
            _insightWindow?.Close();
    }

    private async Task ShowSignatureHelpAsync(CodeEditorViewModel vm, Position position, CancellationToken token, string? triggerCharacter)
    {
        try
        {
            // The document change is debounced 300ms, but the server needs the current source
            // to scan for the call context. Flush immediately before the signatureHelp request.
            await vm.FlushDocumentAsync(token);
            if (token.IsCancellationRequested) return;

            var help = await vm.RequestSignatureHelpAsync(position, token, triggerCharacter);
            if (token.IsCancellationRequested) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                if (help is null || help.Signatures.Length == 0)
                {
                    _insightWindow?.Close();
                    return;
                }

                _insightWindow?.Close();
                var provider = new VbSignatureProvider(help);
                _insightWindow = new OverloadInsightWindow(TextEditor.TextArea)
                {
                    Provider = provider
                };
                _insightWindow.Closed += (_, _) => _insightWindow = null;
                _insightWindow.Show();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[signatureHelp] {ErrorMessage}", ex.Message);
        }
    }

    // ── Document highlights ───────────────────────────────────────────────────

    private void OnCaretMovedForHighlight(object? sender, EventArgs e)
    {
        if (DataContext is not CodeEditorViewModel vm) return;

        _highlightRenderer?.Clear();

        _highlightCts?.Cancel();
        _highlightCts?.Dispose();
        _highlightCts = new CancellationTokenSource();
        var token = _highlightCts.Token;

        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _ = RequestHighlightsAsync(vm, lspPos, token);
    }

    private async Task RequestHighlightsAsync(CodeEditorViewModel vm, Position position, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            if (token.IsCancellationRequested) return;

            var highlights = await vm.RequestDocumentHighlightAsync(position, token);
            if (token.IsCancellationRequested || highlights is null || highlights.Length == 0) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || _highlightRenderer is null) return;
                var doc = TextEditor.Document;
                var segments = new List<(int, int)>();
                foreach (var h in highlights)
                {
                    var startLine = doc.GetLineByNumber(h.Range.Start.Line + 1);
                    var endLine   = doc.GetLineByNumber(h.Range.End.Line + 1);
                    int start = startLine.Offset + h.Range.Start.Character;
                    int end   = endLine.Offset   + h.Range.End.Character;
                    if (start >= 0 && end <= doc.TextLength && start <= end)
                        segments.Add((start, end));
                }
                _highlightRenderer.SetHighlights(segments);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[highlight] {ErrorMessage}", ex.Message);
        }
    }

    // ── Go to definition (F12) and declaration (Ctrl+F12) ─────────────────────

    public void GoToDefinition() => GoTo("definition", (vm, p) => vm.RequestDefinitionAsync(p));

    /// <summary>
    /// Where the symbol is DECLARED, which is a different question from where it is defined.
    /// </summary>
    /// <remarks>
    /// Shares everything below with go-to-definition because the answer has the same shape -- a list of
    /// locations -- and only the request differs. A server that does not distinguish them simply never
    /// advertises <c>declarationProvider</c>, so this quietly does nothing rather than duplicating F12.
    /// </remarks>
    public void GoToDeclaration() => GoTo("declaration", (vm, p) => vm.RequestDeclarationAsync(p));

    private void GoTo(string what, Func<CodeEditorViewModel, Position, Task<HexIDE.Lsp.Messages.Location[]?>> request)
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _ = GoToLocationAsync(vm, lspPos, what, request);
    }

    private async Task GoToLocationAsync(
        CodeEditorViewModel vm, Position position, string what,
        Func<CodeEditorViewModel, Position, Task<HexIDE.Lsp.Messages.Location[]?>> request)
    {
        try
        {
            await vm.FlushDocumentAsync();
            var locations = await request(vm, position);

            if (locations is not { Length: > 0 })
                return;

            var loc = locations[0];
            var targetLine = loc.Range.Start.Line + 1; // AvaloniaEdit is 1-based
            var targetCol  = loc.Range.Start.Character;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // AreSame, not ==. A server may normalise the URI it echoes — a lower-cased drive letter
                // is the measured case (#236) — and an ordinal compare then reads "same file" as "some
                // other file", quietly sending this through the cross-file branch instead. #236's fix
                // reached the diagnostics path and never reached this one.
                if (HexIDE.Lsp.LspDocumentUri.AreSame(loc.Uri, vm.GetDocumentUriPublic()))
                {
                    // Same file — move caret directly
                    if (targetLine >= 1 && targetLine <= TextEditor.Document.LineCount)
                    {
                        var lineObj = TextEditor.Document.GetLineByNumber(targetLine);
                        TextEditor.CaretOffset = Math.Min(lineObj.Offset + targetCol, lineObj.EndOffset);
                        TextEditor.ScrollToLine(targetLine);
                        TextEditor.Focus();
                    }
                }
                else
                {
                    // Cross-file: ask the VM to navigate (uses IEditorService)
                    vm.NavigateToUri(loc.Uri, targetLine, targetCol);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[{What}] {ErrorMessage}", what, ex.Message);
        }
    }

    // ── Rename symbol (F2) ────────────────────────────────────────────────────

    public void RenameSymbol()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);

        // Extract the word under cursor for the default text
        var oldName = GetWordUnderCaret();
        if (string.IsNullOrEmpty(oldName)) return;

        if (vm.RenameRefusalAt(TextEditor.CaretOffset, oldName) is { } refusal)
        {
            _ = vm.ShowRefusalAsync(refusal);
            return;
        }

        _ = RenameSymbolAsync(vm, lspPos, oldName);
    }

    private string? GetWordUnderCaret() => WordAt(TextEditor.CaretOffset);

    // The VB6 identifier (letters / digits / underscore) spanning a document offset, or null if the offset isn't on
    // one. Shared by Rename (caret offset) and the break-mode data tip (pointer offset).
    private string? WordAt(int offset)
    {
        var doc = TextEditor.Document;
        if (offset < 0 || offset > doc.TextLength) return null;

        int start = offset;
        while (start > 0 && IsVbIdentifierChar(doc.GetCharAt(start - 1)))
            start--;

        int end = offset;
        while (end < doc.TextLength && IsVbIdentifierChar(doc.GetCharAt(end)))
            end++;

        return start < end ? doc.GetText(start, end - start) : null;

        static bool IsVbIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    internal async Task RenameSymbolAsync(CodeEditorViewModel vm, Position position, string oldName)
    {
        try
        {
            var newName = await vm.ShowRenameInputBoxAsync(oldName);
            if (newName is null || newName == oldName) return;
            // Before the request, so no server is asked to write a name VB6 would refuse (#273 task 4.4). An empty
            // answer is refused with the rest rather than taken for Cancel, which answers null.
            if (vm.NewNameRefusal(newName) is { } notAName)
            {
                await vm.ShowRefusalAsync(notAName);
                return;
            }

            await vm.FlushDocumentAsync();
            var result = await vm.RequestRenameAsync(position, newName);
            if (result?.Refusal is { } serverRefusal)
            {
                await vm.ShowRefusalAsync(serverRefusal);
                return;
            }
            var edit = result?.Edit;

            if (edit?.Changes is null) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (vm.ApplyRename(edit, oldName) is { } refusal)
                    _ = vm.ShowRefusalAsync(refusal);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[rename] {ErrorMessage}", ex.Message);
        }
    }

    // ── Format document (Shift+Alt+F) ─────────────────────────────────────────

    public void FormatDocument()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        _ = FormatDocumentAsync(vm);
    }

    private async Task FormatDocumentAsync(CodeEditorViewModel vm)
    {
        try
        {
            await vm.FlushDocumentAsync();
            var edits = await vm.RequestFormattingAsync();

            if (edits.Length == 0) return;

            // Through the view model's guarded path, which Format on Save shares, never onto the document as
            // sent: a whole-document answer would otherwise rewrite the header (#273 phase 3).
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => vm.ApplyFormatting(edits));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[formatting] {ErrorMessage}", ex.Message);
        }
    }
}

/// <summary>Wraps an LSP <see cref="CompletionItem"/> as AvaloniaEdit <see cref="ICompletionData"/>.</summary>
/// <param name="item">The server's completion item.</param>
/// <param name="isReadOnlyRegion">
/// Whether a span of the buffer is one only the IDE may write. A completion commit writes the document
/// directly, so the read-only section provider never sees it (#273 task 3.9).
/// </param>
internal sealed class VbCompletionData(CompletionItem item, Func<int, int, bool> isReadOnlyRegion) : ICompletionData
{
    public string Text => item.Label;
    public object Content => item.Label;

    // What an item reads as to the automation surface, whose ListBoxItem holds this object rather than the text: an item
    // was named by its position alone, and selecting one by its text found none (#942 review).
    public override string ToString() => Text;
    public object? Description => item.Detail;
    public double Priority => 0;
    public IImage? Image => null;

    public void Complete(AvaloniaEdit.Editing.TextArea textArea, ISegment completionSegment, EventArgs e)
    {
        // Nothing is written into a region, nor into a file held read-only as a whole (the view passes
        // CodeEditorViewModel.RefusesTyping). A completion belongs to the word at the caret, so there is no
        // "after the region" to move it to that would still mean anything: it is simply not applied.
        if (isReadOnlyRegion(completionSegment.Offset, completionSegment.Length))
            return;
        textArea.Document.Replace(completionSegment, item.InsertText ?? item.Label);
    }
}

/// <summary>
/// Implements <see cref="IOverloadProvider"/> over an LSP <see cref="SignatureHelp"/> response
/// for display in AvaloniaEdit's <see cref="OverloadInsightWindow"/>.
/// </summary>
internal sealed class VbSignatureProvider : IOverloadProvider
{
    private readonly SignatureHelp _help;
    private int _selectedIndex;

    public VbSignatureProvider(SignatureHelp help)
    {
        _help = help;
        _selectedIndex = help.ActiveSignature ?? 0;
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = Math.Clamp(value, 0, _help.Signatures.Length - 1);
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedIndex)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentHeader)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentContent)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentIndexText)));
        }
    }

    public int Count => _help.Signatures.Length;

    public string CurrentIndexText => $"{_selectedIndex + 1} of {_help.Signatures.Length}";

    public object? CurrentHeader
    {
        get
        {
            var sig = _help.Signatures[_selectedIndex];
            var activeParam = _help.ActiveParameter ?? 0;

            if (sig.Parameters is null || sig.Parameters.Length == 0 || activeParam >= sig.Parameters.Length)
                return sig.Label;

            var label = sig.Label;
            if (sig.Parameters[activeParam].Label.RangeIn(label) is not { } range) return label;
            return new TextBlock
            {
                Inlines =
                [
                    new Run(label[..range.Start]),
                    new Run(label.Substring(range.Start, range.Length)) { FontWeight = FontWeight.Bold },
                    new Run(label[(range.Start + range.Length)..])
                ]
            };
        }
    }

    public object? CurrentContent
    {
        get
        {
            var sig = _help.Signatures[_selectedIndex];
            var activeParam = _help.ActiveParameter ?? 0;
            if (sig.Parameters is not null && activeParam < sig.Parameters.Length)
            {
                var p = sig.Parameters[activeParam];
                if (p.Documentation is not null)
                    return $"{p.Label.TextIn(sig.Label)}: {p.Documentation}";
                return p.Label.TextIn(sig.Label);
            }
            return sig.Documentation;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
