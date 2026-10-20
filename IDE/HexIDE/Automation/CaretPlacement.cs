using HexIDE.Forms.ViewModels;

namespace HexIDE.Automation;

/// <summary>
/// Puts a code window's caret at a line and column, as the code window numbers them, for open_file (#700).
/// </summary>
public static class CaretPlacement
{
    /// <summary>
    /// The caret moved to (<paramref name="line"/>, <paramref name="column"/>), both 1-based, and a note saying where
    /// it now is; or why not, with the caret left where it was.
    /// </summary>
    /// <remarks>
    /// Refused rather than clamped, unlike a language server's position: a caret quietly put somewhere else would take
    /// the caller's next type_text there. A column one past the line's last character is its end, where typing appends.
    /// </remarks>
    public static (string? Placed, string? Refusal) Place(CodeEditorViewModel editor, int line, int column)
    {
        var lines = editor.Document.LineCount;
        if (line < 1 || line > lines)
            return (null, $"it has {lines} lines, so line {line} is not one of them; the caret was left where it was");
        var docLine = editor.Document.GetLineByNumber(line);
        if (column < 1 || column > docLine.Length + 1)
            return (null, $"line {line} has {docLine.Length} characters, so column {column} is not on it (column "
                          + $"{docLine.Length + 1} is its end); the caret was left where it was");
        editor.CaretOffset = docLine.Offset + column - 1;
        return ($"The caret is at line {line}, column {column}: offset {editor.CaretOffset} in the text get_file_content "
                + "returns.", null);
    }
}
