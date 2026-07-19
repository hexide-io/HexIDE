namespace HexIDE.Addins;

public interface IEditorAccess
{
    AddinDocument? GetActiveDocument();
    AddinSelection? GetSelection();
    void NavigateTo(string fileName, int line, int column);
    Task SetContent(string fileName, string content);
    Task ApplyEdits(string fileName, IReadOnlyList<AddinTextEdit> edits);
}
