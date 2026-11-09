using Avalonia.Controls;

namespace HexIDE.Tools.TranslationEditor;

public partial class TranslationEditorView : UserControl
{
    public TranslationEditorView()
    {
        InitializeComponent();
    }

    private void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is TranslationRowViewModel { IsProtected: true } row)
        {
            e.Cancel = true;
            (DataContext as TranslationEditorViewModel)?.ExplainProtected(row);
        }
    }
}
