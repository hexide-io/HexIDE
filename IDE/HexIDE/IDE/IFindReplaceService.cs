namespace HexIDE.IDE;

/// <summary>
/// Manages the modeless Find/Replace dialog.
/// </summary>
public interface IFindReplaceService
{
    /// <summary>Opens the Find dialog (or activates it if already open).</summary>
    void ShowFind();

    /// <summary>Opens the Replace dialog (or switches to Replace mode if already open).</summary>
    void ShowReplace();

    /// <summary>Repeats the last find operation (F3).</summary>
    void FindNext();
}
