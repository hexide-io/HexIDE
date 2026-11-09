using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit;
using HexIDE.Forms.ViewModels;
using HexIDE.Themes;

namespace HexIDE.Forms.Views;

public partial class ExportPreviewDialog : UserControl
{
    public ExportPreviewDialog()
    {
        InitializeComponent();

        // Assigned in code because the definition is loaded and theme-corrected once for the process, and
        // because a null one has to be tolerated: colouring is a nicety, and the point of this dialog is
        // that the bytes are on screen.
        if (this.FindControl<TextEditor>("Preview") is { } editor)
            editor.SyntaxHighlighting = ProtocolHighlighting.Definition;

        // The preview has the keyboard focus when this opens, and Esc stops there, where AvaloniaEdit binds it to closing a
        // search panel, so it would never reach the Cancel button (#3382).
        AddHandler(KeyDownEvent, CancelOnEscape, RoutingStrategies.Tunnel);
    }

    private void CancelOnEscape(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None
            || DataContext is not ExportPreviewDialogViewModel { CancelCommand: { } cancel } || !cancel.CanExecute(null))
            return;
        e.Handled = true;
        cancel.Execute(null);
    }
}
