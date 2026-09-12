using Avalonia.Controls;
using AvaloniaEdit;
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
    }
}
