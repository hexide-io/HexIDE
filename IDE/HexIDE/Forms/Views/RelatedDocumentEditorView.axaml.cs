using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using Avalonia;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.Themes;
using Serilog;

namespace HexIDE.Forms.Views;

public partial class RelatedDocumentEditorView : UserControl
{
    private Action? _onPaletteChanged;
    private LspTextMarkerService? _markerService;
    private LspDiagnosticsColorizer? _colorizer;
    private System.ComponentModel.PropertyChangedEventHandler? _vmCaretSync;
    private EventHandler? _caretMoved;

    public RelatedDocumentEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyHighlighting();

        // A theme switch re-tints the shared definition, but AvaloniaEdit has already baked the old
        // brushes into the visual lines it built — Redraw is what clears that cache. Same reason the VB6
        // editor subscribes, and it is what makes an ALREADY-OPEN document recolour rather than needing
        // to be closed and reopened.
        AttachedToVisualTree += (_, _) =>
        {
            _onPaletteChanged = () => this.FindControl<TextEditor>("TextEditor")?.TextArea.TextView.Redraw();
            SyntaxHighlightingTheme.PaletteChanged += _onPaletteChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_onPaletteChanged is not null)
                SyntaxHighlightingTheme.PaletteChanged -= _onPaletteChanged;
            _onPaletteChanged = null;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Draws whatever language server claims this file — the last step of giving a carried file language
    /// support at all.
    ///
    /// <para>
    /// The same two renderers the VB6 editor uses, deliberately: a diagnostic should look the same
    /// wherever it appears, and the colorizer already limits its red text to errors, so a linter's
    /// warnings on prose stay squiggles rather than turning a paragraph red.
    /// </para>
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        var editor = this.FindControl<TextEditor>("TextEditor");
        if (editor is null || DataContext is not RelatedDocumentEditorViewModel vm) return;

        _markerService = new LspTextMarkerService(editor);
        editor.TextArea.TextView.BackgroundRenderers.Add(_markerService);
        _colorizer = new LspDiagnosticsColorizer(editor.TextArea.TextView);
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        vm.MarkersChanged += OnMarkersChanged;

        // Caught up rather than waiting for the next publish. Moving this document to another dock
        // detaches and re-materialises the view, and a server has no reason to re-publish for a document
        // that has not changed — so without this the squiggles would vanish on a dock move and stay gone.
        OnMarkersChanged(vm.Markers);

        AttachCaretSync(editor, vm);
    }

    /// <summary>
    /// Two-way caret and selection, which is what makes Find visible in this editor rather than merely
    /// successful.
    ///
    /// <para>
    /// Both halves are load-bearing. Outbound (view model → editor) is how a match gets highlighted and
    /// scrolled into view; inbound (editor → view model) is how the NEXT Find Next starts from where the
    /// user actually is rather than from the top of the file every time.
    /// </para>
    ///
    /// <para>
    /// Handlers are kept in fields so they can be removed on detach: a Dock document-move re-materialises
    /// this view against the SAME persistent view model, so an unremoved handler would accumulate on it —
    /// the defect the VB6 editor's own selection sync already carries a note about.
    /// </para>
    /// </summary>
    private void AttachCaretSync(TextEditor editor, RelatedDocumentEditorViewModel vm)
    {
        _vmCaretSync = (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(RelatedDocumentEditorViewModel.SelectionStart):
                case nameof(RelatedDocumentEditorViewModel.SelectionLength):
                    // Clamped rather than trusted. A cross-document search writes SelectionStart and
                    // SelectionLength one at a time, so this runs once on a half-applied pair whose start
                    // belongs to the new match and whose length still belongs to the old one — and
                    // TextEditor.Select throws on a range that runs past the end of the document.
                    var start = Math.Clamp(vm.SelectionStart, 0, editor.Document.TextLength);
                    var length = Math.Clamp(vm.SelectionLength, 0, editor.Document.TextLength - start);
                    if (editor.SelectionStart != start || editor.SelectionLength != length)
                    {
                        editor.Select(start, length);
                        if (length > 0)
                            editor.TextArea.Caret.BringCaretToView();
                    }
                    break;

                case nameof(RelatedDocumentEditorViewModel.CaretOffset):
                    var caret = Math.Clamp(vm.CaretOffset, 0, editor.Document.TextLength);
                    if (editor.CaretOffset != caret)
                        editor.CaretOffset = caret;
                    break;
            }
        };
        vm.PropertyChanged += _vmCaretSync;

        _caretMoved = (_, _) => vm.CaretOffset = editor.CaretOffset;
        editor.TextArea.Caret.PositionChanged += _caretMoved;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (DataContext is RelatedDocumentEditorViewModel vm)
        {
            vm.MarkersChanged -= OnMarkersChanged;
            if (_vmCaretSync is not null) vm.PropertyChanged -= _vmCaretSync;
        }

        // Removed from the renderer lists too, not merely dropped. The view is re-attachable, and a
        // second attach would otherwise stack another pair on top of the first — every diagnostic drawn
        // twice, and the stale set never cleared.
        var editor = this.FindControl<TextEditor>("TextEditor");
        if (editor is not null)
        {
            if (_markerService is not null) editor.TextArea.TextView.BackgroundRenderers.Remove(_markerService);
            if (_colorizer is not null) editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
            if (_caretMoved is not null) editor.TextArea.Caret.PositionChanged -= _caretMoved;
        }

        _markerService = null;
        _colorizer = null;
        _vmCaretSync = null;
        _caretMoved = null;
    }

    private void OnMarkersChanged(IReadOnlyList<LspMarker> markers)
    {
        _markerService?.SetMarkers(markers);
        _colorizer?.SetMarkers(markers);
    }

    /// <summary>
    /// Chooses the highlighting from the document's own extension.
    ///
    /// <para>
    /// The VB6 editor sets <c>SyntaxHighlighting</c> to one fixed definition unconditionally, which is
    /// right when every document it can open is VB6 source and wrong the moment one is not — a Markdown
    /// file rendered with VB6 colouring looks broken in a way that reads as a bug in the file.
    /// </para>
    ///
    /// <para>
    /// Resolution is by extension through AvaloniaEdit's own registry, so it covers whatever that ships
    /// rather than a list maintained here. No match means no highlighting, which is the correct answer for
    /// a plain text file and not a failure.
    /// </para>
    /// </summary>
    private void ApplyHighlighting()
    {
        var editor = this.FindControl<TextEditor>("TextEditor");
        if (editor is null) return;

        var path = (DataContext as RelatedDocumentEditorViewModel)?.RelatedDocument?.AbsolutePath;
        var extension = path is null ? null : Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
        {
            editor.SyntaxHighlighting = null;
            return;
        }

        try
        {
            var definition = HighlightingManager.Instance.GetDefinitionByExtension(extension);
            // Bundled definitions hardcode a light palette, so they are unreadable on a dark background
            // until adopted — the same defect the VB6 definition had before it was given a dark palette.
            SyntaxHighlightingTheme.Adopt(definition);
            editor.SyntaxHighlighting = definition;
        }
        catch (Exception ex)
        {
            // Colouring is a nicety; never let a missing or malformed definition stop the file opening.
            Log.Debug(ex, "No syntax highlighting for extension {Extension}", extension);
            editor.SyntaxHighlighting = null;
        }
    }
}
