using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Labs.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using HexIDE.Bookmarks;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Editor;
using HexIDE.Utils;
using R3;
using Serilog;

namespace HexIDE.Forms.Views;

public partial class CodeEditorView : UserControl
{
    private IDisposable? sub;
    private LspTextMarkerService? _markerService;
    private LspDiagnosticsColorizer? _colorizer;
    private CancellationTokenSource? _hoverCts;
    private BookmarkMargin? _bookmarkMargin;
    private Point _lastHoverPoint;
    private FoldingManager? _foldingManager;
    private CancellationTokenSource? _foldCts;
    private CompletionWindow? _completionWindow;
    private CancellationTokenSource? _completionCts;
    private OverloadInsightWindow? _insightWindow;
    private CancellationTokenSource? _signatureCts;
    private DocumentHighlightRenderer? _highlightRenderer;
    private CancellationTokenSource? _highlightCts;
    private HexIDE.Debugging.BreakpointMargin? _breakpointMargin;
    private HexIDE.Debugging.CurrentLineRenderer? _currentLineRenderer;
    private HexIDE.Runtime.Debugging.IDebugController? _debugController;
    private Action<HexIDE.Runtime.Debugging.StoppedInfo>? _onDebugStopped;
    private Action? _onDebugContinued;
    private string? _debugModuleName;
    private System.ComponentModel.PropertyChangedEventHandler? _vmSelectionSync;
    private bool _resetPromptOpen;
    private string? _preEditSnapshot;   // document text captured before the first edit while running (for No→revert)
    private int _preEditCaret;

    public DelegateCommand Undo { get; }
    public DelegateCommand Redo { get; }
    public DelegateCommand Copy { get; }
    public DelegateCommand Cut { get; }
    public DelegateCommand Delete { get; }
    public DelegateCommand Paste { get; }
    public DelegateCommand Find { get; }
    public DelegateCommand Replace { get; }
    public DelegateCommand SelectAll { get; }

    static CodeEditorView()
    {
        var uri = new Uri("avares://HexIDE/Resources/TextHighlighting/VB6.xshd.xml");

        // Get the asset loader from Avalonia
        var xshdContent = AssetLoader.Open(uri);
        
        if (xshdContent == null)
            throw new InvalidOperationException("VB6 XSHD resource not found");

        using var reader = new XmlTextReader(xshdContent);
        var xshd = HighlightingLoader.LoadXshd(reader);
        var highlightingDefinition = HighlightingLoader.Load(xshd, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting("VB6", new[] { ".vb6", ".bas", ".frm", ".cls" }, highlightingDefinition);
    }
    
    public CodeEditorView()
    {
        Undo = new DelegateCommand(() => TextEditor.Undo(), () => TextEditor?.CanUndo ?? false);
        Redo = new DelegateCommand(() => TextEditor.Redo(), () =>
            TextEditor?.CanRedo ?? false);
        Copy = new DelegateCommand(() => TextEditor.Copy(), () => true);
        Cut = new DelegateCommand(() => TextEditor.Cut(), () => true);
        Delete = new DelegateCommand(() => TextEditor.Delete(), () => true);
        Paste = new DelegateCommand(() => TextEditor.Paste(), () => true);
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
        EditingCommands.TabForward.Execute(null, TextEditor.TextArea);
    }

    public void Outdent()
    {
        EditingCommands.TabBackward.Execute(null, TextEditor.TextArea);
    }

    public void TriggerCompletion() => TriggerCompletionAsync();

    public void TriggerSignatureHelp() => TriggerSignatureHelpAsync();

    public async Task InsertFile()
    {
        // Get top level from the current control. Alternatively, you can use Window reference instead.
        var topLevel = TopLevel.GetTopLevel(this);

        if (topLevel == null)
            return;
        
        // Start async operation to open the dialog.
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Text File",
            AllowMultiple = false
        });

        if (files.Count >= 1)
        {
            await using var stream = await files[0].OpenReadAsync();
            using var streamReader = new StreamReader(stream);
            var fileContent = await streamReader.ReadToEndAsync();
            TextEditor.SelectedText = fileContent;
        }
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
                    if (TextEditor.CaretOffset != offset)
                        TextEditor.CaretOffset = offset;
                });

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

            // Attach LSP text marker service (squiggles) and diagnostics colorizer (red text)
            _markerService = new LspTextMarkerService(TextEditor);
            TextEditor.TextArea.TextView.BackgroundRenderers.Add(_markerService);
            _colorizer = new LspDiagnosticsColorizer(TextEditor.TextArea.TextView);
            TextEditor.TextArea.TextView.LineTransformers.Add(_colorizer);
            vm.MarkersChanged += OnMarkersChanged;

            // Document highlights (background renderer, behind squiggles)
            _highlightRenderer = new DocumentHighlightRenderer(TextEditor);
            TextEditor.TextArea.TextView.BackgroundRenderers.Insert(0, _highlightRenderer);
            TextEditor.TextArea.Caret.PositionChanged += OnCaretMovedForHighlight;

            // Code folding
            _foldingManager = FoldingManager.Install(TextEditor.TextArea);
            _ = RequestFoldingsAsync(vm, CancellationToken.None);

            // Bookmark gutter margin
            _bookmarkMargin = new BookmarkMargin(vm.BookmarkService, vm.GetDocumentUriPublic());
            TextEditor.TextArea.LeftMargins.Insert(0, _bookmarkMargin);

            // Debugger: breakpoint gutter (red dots, click-to-toggle) + the amber current-statement bar.
            var docUri = vm.GetDocumentUriPublic();
            _debugModuleName = docUri[(docUri.LastIndexOf('/') + 1)..]; // vb6://form/Form1 → Form1
            _breakpointMargin = new HexIDE.Debugging.BreakpointMargin(vm.BreakpointService, docUri);
            TextEditor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
            _currentLineRenderer = new HexIDE.Debugging.CurrentLineRenderer(TextEditor);
            TextEditor.TextArea.TextView.BackgroundRenderers.Insert(0, _currentLineRenderer);

            _debugController = vm.DebugController;
            _onDebugStopped = info => ShowCurrentStatement(info.Module, info.Line);
            _onDebugContinued = () => _currentLineRenderer?.SetLine(null);
            _debugController.Stopped += _onDebugStopped;
            _debugController.Continued += _onDebugContinued;
            // If the interpreter is already paused (this tab was opened by the Stopped bridge after the break),
            // show the current statement immediately rather than waiting for the next event.
            if (_debugController.CurrentStop is { } stop)
                ShowCurrentStatement(stop.Module, stop.Line);

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

            TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("VB6");

            // Minimap (prototype)
            Minimap.Attach(TextEditor, HighlightingManager.Instance.GetDefinition("VB6"));

            // Apply tab width from settings
            TextEditor.Options.IndentationSize = vm.Settings.TabWidth;
            TextEditor.Options.ConvertTabsToSpaces = true;

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
    private void ShowCurrentStatement(string module, int line)
    {
        if (_currentLineRenderer is null)
            return;
        if (!string.Equals(module, _debugModuleName, StringComparison.OrdinalIgnoreCase))
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

        if (DataContext is CodeEditorViewModel vm)
        {
            vm.FocusWindowRequest -= VmOnFocusWindowRequest;
            vm.MarkersChanged -= OnMarkersChanged;
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

        if (_foldingManager is not null)
        {
            FoldingManager.Uninstall(_foldingManager);
            _foldingManager = null;
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

        if (_debugController is not null)
        {
            if (_onDebugStopped is not null) _debugController.Stopped -= _onDebugStopped;
            if (_onDebugContinued is not null) _debugController.Continued -= _onDebugContinued;
            _debugController = null;
            _onDebugStopped = null;
            _onDebugContinued = null;
        }

        _foldCts?.Cancel();
        _foldCts?.Dispose();
        _foldCts = null;

        TextEditor.PointerMoved -= OnPointerMoved;
        TextEditor.PointerExited -= OnPointerExited;
        TextEditor.TextArea.TextEntered  -= OnTextEntered;
        TextEditor.TextArea.TextEntering -= OnTextEntering;
        TextEditor.TextArea.KeyDown      -= OnEditorKeyDown;
        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = null;

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
        if (DataContext is not CodeEditorViewModel vm) return;

        _foldCts?.Cancel();
        _foldCts?.Dispose();
        _foldCts = new CancellationTokenSource();
        var token = _foldCts.Token;
        _ = FoldAfterDelayAsync(vm, token);
    }

    private async Task FoldAfterDelayAsync(CodeEditorViewModel vm, CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            await RequestFoldingsAsync(vm, token);
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
                Vb6FoldingAdapter.Apply(_foldingManager, doc, ranges);
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

        ToolTip.SetIsOpen(TextEditor, false);

        var tvPos = TextEditor.TextArea.TextView.GetPosition(point);
        if (tvPos is null) return;

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
        if (!vm.Settings.AutoQuickInfo) return;

        _ = HoverAfterDelayAsync(vm, lspPos, token);
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
                    ToolTip.SetIsOpen(TextEditor, false);
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

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _hoverCts?.Cancel();
        ToolTip.SetIsOpen(TextEditor, false);
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

    // ── Bookmarks ─────────────────────────────────────────────────────────────

    public void ToggleBookmark()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        int line = TextEditor.TextArea.Caret.Line - 1; // 0-based
        vm.BookmarkService.Toggle(vm.GetDocumentUriPublic(), line);
    }

    public void NextBookmark()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        int current = TextEditor.TextArea.Caret.Line - 1;
        var target = vm.BookmarkService.NextBookmark(vm.GetDocumentUriPublic(), current);
        if (target.HasValue)
            JumpToBookmarkLine(target.Value);
    }

    public void PreviousBookmark()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        int current = TextEditor.TextArea.Caret.Line - 1;
        var target = vm.BookmarkService.PreviousBookmark(vm.GetDocumentUriPublic(), current);
        if (target.HasValue)
            JumpToBookmarkLine(target.Value);
    }

    public void ClearAllBookmarks()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        vm.BookmarkService.ClearAll(vm.GetDocumentUriPublic());
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
            await ShowHoverAsync(vm, pos, token);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ShowHoverAsync(CodeEditorViewModel vm, Position pos, CancellationToken token)
    {
        try
        {
            var result = await vm.RequestHoverAsync(pos, token);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                if (result is { Contents.Value.Length: > 0 })
                {
                    ToolTip.SetTip(TextEditor, result.Contents.Value);
                    ToolTip.SetIsOpen(TextEditor, true);
                }
                else
                {
                    ToolTip.SetIsOpen(TextEditor, false);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[hover] {ErrorMessage}", ex.Message);
        }
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
        // pop VB6's reset prompt — Yes keeps the edit + resets the project, No reverts it (see ShowResetPromptAsync).
        if (e.Text is { Length: > 0 })
            MaybeStartResetPrompt();
        // If a completion window is open and the user types a character that can't
        // be part of an identifier, close the window (AvaloniaEdit will then insert the char).
        if (_completionWindow is null || e.Text is not { Length: > 0 }) return;
        var ch = e.Text[0];
        if (!char.IsLetterOrDigit(ch) && ch != '_')
            _completionWindow.Close();
    }

    // VB6-faithful Edit-and-Continue affordance: the interpreter can't apply an edit to a running program, so a code
    // edit while running/paused pops VB6's "This action will reset your project" prompt. The edit is NOT cancelled
    // (VB6 lets it appear); the pre-edit document is snapshotted here (this fires before the edit is applied) so a
    // "No" answer can revert every edit made while the prompt was open. Fired once, guarded against keystroke stacking.
    private void MaybeStartResetPrompt()
    {
        if (_resetPromptOpen || DataContext is not CodeEditorViewModel vm || !vm.IsProjectRunning)
            return;
        _resetPromptOpen = true;
        _preEditSnapshot = TextEditor.Document.Text;
        _preEditCaret = TextEditor.CaretOffset;
        _ = ShowResetPromptAsync(vm);
    }

    private async Task ShowResetPromptAsync(CodeEditorViewModel vm)
    {
        try
        {
            var reset = await vm.ConfirmResetWhileRunningAsync();
            // No → undo every edit made since the prompt opened, back to the pre-edit state. Yes → keep the edit
            // (the run reset was already requested), matching VB6.
            if (!reset && _preEditSnapshot is { } snapshot)
            {
                TextEditor.Document.Text = snapshot;
                TextEditor.CaretOffset = System.Math.Min(_preEditCaret, snapshot.Length);
            }
        }
        finally
        {
            _resetPromptOpen = false;
            _preEditSnapshot = null;
        }
    }

    // Keys that would modify the document (printable chars arrive via OnTextEntering, not here).
    private static bool IsDocumentEditKey(KeyEventArgs e) => e.Key switch
    {
        Key.Back or Key.Delete or Key.Return or Key.Enter or Key.Tab
            => e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift,
        Key.V or Key.X   // paste / cut
            => e.KeyModifiers.HasFlag(KeyModifiers.Control),
        _ => false,
    };

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (e.Text is not { Length: > 0 }) return;
        var ch = e.Text[0];
        // Trigger completion on any letter (start/continuation of a word)
        if (char.IsLetter(ch))
            TriggerCompletionAsync();
        // Trigger signature help on ( or ,
        if (ch == '(' || ch == ',')
            TriggerSignatureHelpAsync();
        // Close signature help when all parens are closed
        if (ch == ')')
            CloseInsightWindowIfBalanced();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Editing while the project is running/paused → VB6 reset prompt (see MaybeStartResetPrompt). Covers the
        // non-printable edit keys (Backspace/Delete/Enter/Tab/Ctrl+V/Ctrl+X); printable chars go via OnTextEntering.
        // The edit is allowed to proceed (VB6 lets it land); No reverts it, Yes keeps it + resets the project.
        if (IsDocumentEditKey(e))
            MaybeStartResetPrompt();
        // Enter = auto-close block + auto-indent
        if (e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            if (HandleEnterAutoClose())
                e.Handled = true;
        }
        // F12 = go to definition
        else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            GoToDefinition();
        }
        // Shift+Alt+F = format document
        else if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Shift | KeyModifiers.Alt))
        {
            e.Handled = true;
            FormatDocument();
        }
    }

    /// <summary>
    /// Handles Enter key press: if the current logical line is a VB6 block opener,
    /// inserts the closing statement and positions the cursor between them.
    /// Otherwise, inserts a newline with auto-indent (matching current line).
    /// </summary>
    /// <returns>True if the event was handled.</returns>
    private bool HandleEnterAutoClose()
    {
        try
        {
            var doc = TextEditor.Document;
            var caret = TextEditor.TextArea.Caret;
            var caretOffset = caret.Offset;
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
                InsertNewlineWithIndent(doc, caretOffset, currentLineText);
                return true;
            }

            // If the line ends with _ (continuation), just auto-indent (no case-correct yet)
            if (VbAutoCloseProvider.IsContinuationLine(currentLineText))
            {
                Log.Debug("[auto-close] Continuation line, auto-indent only");
                InsertNewlineWithIndent(doc, caretOffset, currentLineText);
                return true;
            }

            // Find the first physical line of the logical line (for continuations)
            var startLineNumber = currentLineNumber;
            while (startLineNumber > 1)
            {
                var prevLine = doc.GetLineByNumber(startLineNumber - 1);
                if (!VbAutoCloseProvider.IsContinuationLine(
                        doc.GetText(prevLine.Offset, prevLine.Length)))
                    break;
                startLineNumber--;
            }

            // Assemble the full logical line (handle multi-line continuations)
            var logicalLine = VbAutoCloseProvider.AssembleLogicalLine(
                lineIdx =>
                {
                    var dl = doc.GetLineByNumber(lineIdx + 1); // lineIdx is 0-based, doc is 1-based
                    return doc.GetText(dl.Offset, dl.Length);
                },
                currentLineNumber - 1); // Convert to 0-based index

            var closer = VbAutoCloseProvider.GetClosingStatement(logicalLine);
            Log.Debug("[auto-close] Logical line: '{LogicalLine}', closer: {Closer}",
                logicalLine, closer ?? "(null)");

            // Keyword-normalize the committed physical lines + insert closer (single undo)
            doc.BeginUpdate();
            try
            {
                // 1. Case-correct all physical lines of the committed logical line
                var offsetAdjustment = 0;
                for (var lineNum = startLineNumber; lineNum <= currentLineNumber; lineNum++)
                {
                    var dl = doc.GetLineByNumber(lineNum);
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

                if (closer is null || NextNonEmptyLineIs(doc, currentLineNumber, closer))
                {
                    // No closer to insert — just newline + auto-indent
                    var updatedLine = doc.GetLineByNumber(currentLineNumber);
                    var updatedLineText = doc.GetText(updatedLine.Offset, updatedLine.Length);
                    var indent = VbAutoCloseProvider.GetIndentation(updatedLineText);
                    var insertText = "\n" + indent;
                    doc.Insert(adjustedCaretOffset, insertText);
                    caret.Offset = adjustedCaretOffset + insertText.Length;
                }
                else
                {
                    // Insert closer
                    var updatedLine = doc.GetLineByNumber(currentLineNumber);
                    var updatedLineText = doc.GetText(updatedLine.Offset, updatedLine.Length);
                    var indent = VbAutoCloseProvider.GetIndentation(updatedLineText);
                    var bodyIndent = VbAutoCloseProvider.GetBodyIndent(indent);
                    var insertText = "\n" + bodyIndent + "\n" + indent + closer;

                    Log.Debug("[auto-close] Inserting closer: indent='{Indent}', insertText='{InsertText}'",
                        indent, insertText.Replace("\n", "\\n"));

                    doc.Insert(adjustedCaretOffset, insertText);
                    caret.Offset = adjustedCaretOffset + 1 + bodyIndent.Length;
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

    private void InsertNewlineWithIndent(TextDocument doc, int caretOffset, string currentLineText)
    {
        var indent = VbAutoCloseProvider.GetIndentation(currentLineText);
        var insertText = "\n" + indent;
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

    private void TriggerCompletionAsync()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        if (!vm.Settings.AutoListMembers) return;

        _completionCts?.Cancel();
        _completionCts?.Dispose();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;

        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _ = ShowCompletionAsync(vm, lspPos, token);
    }

    private async Task ShowCompletionAsync(CodeEditorViewModel vm, Position position, CancellationToken token)
    {
        try
        {
            var items = await vm.RequestCompletionAsync(position, token);
            if (token.IsCancellationRequested || items.Length == 0) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                // If a window is already open, close it first
                _completionWindow?.Close();

                _completionWindow = new CompletionWindow(TextEditor.TextArea);
                var data = _completionWindow.CompletionList.CompletionData;
                foreach (var item in items)
                    data.Add(new VbCompletionData(item));

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

    private void TriggerSignatureHelpAsync()
    {
        if (DataContext is not CodeEditorViewModel vm) return;

        _signatureCts?.Cancel();
        _signatureCts?.Dispose();
        _signatureCts = new CancellationTokenSource();
        var token = _signatureCts.Token;

        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _ = ShowSignatureHelpAsync(vm, lspPos, token);
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

    private async Task ShowSignatureHelpAsync(CodeEditorViewModel vm, Position position, CancellationToken token)
    {
        try
        {
            // The document change is debounced 300ms, but the server needs the current source
            // to scan for the call context. Flush immediately before the signatureHelp request.
            await vm.FlushDocumentAsync(token);
            if (token.IsCancellationRequested) return;

            var help = await vm.RequestSignatureHelpAsync(position, token);
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

    // ── Go to definition (F12) ────────────────────────────────────────────────

    public void GoToDefinition()
    {
        if (DataContext is not CodeEditorViewModel vm) return;
        var caret = TextEditor.TextArea.Caret.Position;
        var lspPos = new Position(caret.Line - 1, caret.Column - 1);
        _ = GoToDefinitionAsync(vm, lspPos);
    }

    private async Task GoToDefinitionAsync(CodeEditorViewModel vm, Position position)
    {
        try
        {
            await vm.FlushDocumentAsync();
            var locations = await vm.RequestDefinitionAsync(position);

            if (locations is not { Length: > 0 })
                return;

            var loc = locations[0];
            var targetLine = loc.Range.Start.Line + 1; // AvaloniaEdit is 1-based
            var targetCol  = loc.Range.Start.Character;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (loc.Uri == vm.GetDocumentUriPublic())
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
            Log.Debug("[definition] {ErrorMessage}", ex.Message);
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

    private async Task RenameSymbolAsync(CodeEditorViewModel vm, Position position, string oldName)
    {
        try
        {
            var newName = await vm.ShowInputBoxAsync("New name:", "Rename", oldName);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

            await vm.FlushDocumentAsync();
            var edit = await vm.RequestRenameAsync(position, newName);

            if (edit?.Changes is null) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var docUri = vm.GetDocumentUriPublic();
                if (!edit.Changes.TryGetValue(docUri, out var textEdits)) return;

                // Apply edits in reverse order to preserve offsets
                var sorted = new List<TextEdit>(textEdits);
                sorted.Sort((a, b) =>
                {
                    int cmp = b.Range.Start.Line.CompareTo(a.Range.Start.Line);
                    return cmp != 0 ? cmp : b.Range.Start.Character.CompareTo(a.Range.Start.Character);
                });

                var doc = TextEditor.Document;
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

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Apply edits in reverse order to preserve offsets
                var sorted = new List<TextEdit>(edits);
                sorted.Sort((a, b) =>
                {
                    int cmp = b.Range.Start.Line.CompareTo(a.Range.Start.Line);
                    return cmp != 0 ? cmp : b.Range.Start.Character.CompareTo(a.Range.Start.Character);
                });

                var doc = TextEditor.Document;
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
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Debug("[formatting] {ErrorMessage}", ex.Message);
        }
    }
}

/// <summary>Wraps an LSP <see cref="CompletionItem"/> as AvaloniaEdit <see cref="ICompletionData"/>.</summary>
internal sealed class VbCompletionData(CompletionItem item) : ICompletionData
{
    public string Text => item.Label;
    public object Content => item.Label;
    public object? Description => item.Detail;
    public double Priority => 0;
    public IImage? Image => null;

    public void Complete(AvaloniaEdit.Editing.TextArea textArea, ISegment completionSegment, EventArgs e)
    {
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

            // Highlight the active parameter in the label by surrounding it with brackets
            var paramLabel = sig.Parameters[activeParam].Label;
            var label = sig.Label;
            var idx = label.IndexOf(paramLabel, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return label;

            // Build a TextBlock-like string; AvaloniaEdit accepts plain strings here
            return label[..idx] + "[" + label.Substring(idx, paramLabel.Length) + "]" + label[(idx + paramLabel.Length)..];
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
                    return $"{p.Label}: {p.Documentation}";
                return p.Label;
            }
            return sig.Documentation;
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}