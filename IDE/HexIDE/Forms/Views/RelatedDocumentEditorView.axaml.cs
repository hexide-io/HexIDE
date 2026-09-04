using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using HexIDE.Forms.ViewModels;
using HexIDE.Themes;
using Serilog;

namespace HexIDE.Forms.Views;

public partial class RelatedDocumentEditorView : UserControl
{
    private Action? _onPaletteChanged;

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
